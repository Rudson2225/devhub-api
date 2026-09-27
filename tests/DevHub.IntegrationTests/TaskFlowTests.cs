using System.Net;
using System.Net.Http.Json;
using DevHub.Application.DTOs.Activities;
using DevHub.Application.DTOs.Comments;
using DevHub.Application.DTOs.Dashboard;
using DevHub.Application.DTOs.Tags;
using DevHub.Application.DTOs.Tasks;
using FluentAssertions;

namespace DevHub.IntegrationTests;

public sealed class TaskFlowTests : IClassFixture<DevHubApiFactory>
{
    private readonly DevHubApiFactory _factory;

    public TaskFlowTests(DevHubApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Full_task_workflow_works_end_to_end()
    {
        var owner = await _factory.RegisterAsync("task-owner@test.com");

        var project = await _factory.CreateProjectAsync(owner.AccessToken, "Task project");

        var createTask = await _factory.HttpClient.SendAsync(
            _factory.Authenticated(
                new HttpRequestMessage(HttpMethod.Post, $"/api/projects/{project.Id}/tasks")
                {
                    Content = JsonContent.Create(new
                    {
                        title = "Implement auth",
                        description = "JWT refresh flow",
                        priority = "High",
                        status = "Todo",
                        assigneeId = owner.User.Id
                    })
                },
                owner.AccessToken));

        createTask.StatusCode.Should().Be(HttpStatusCode.Created);
        var task = (await createTask.Content.ReadFromJsonAsync<TaskResponse>(DevHubApiFactory.ApiJsonOptions))!;
        task.Title.Should().Be("Implement auth");
        task.AssigneeId.Should().Be(owner.User.Id);
        task.ReporterName.Should().Be("task-owner");
        task.Status.Should().Be(DevHub.Domain.Enums.ProjectTaskStatus.Todo);

        var board = await _factory.HttpClient.SendAsync(
            _factory.Authenticated(new HttpRequestMessage(HttpMethod.Get, $"/api/projects/{project.Id}/tasks/board"), owner.AccessToken));
        board.StatusCode.Should().Be(HttpStatusCode.OK);
        var kanban = (await board.Content.ReadFromJsonAsync<TaskKanbanResponse>(DevHubApiFactory.ApiJsonOptions))!;
        kanban.Todo.Should().ContainSingle(t => t.Id == task.Id);

        var tag1 = await CreateTagAsync(owner.AccessToken, project.Id, "bug", "#ff0000");
        var tag2 = await CreateTagAsync(owner.AccessToken, project.Id, "feature", "#00ff00");
        tag1.Name.Should().Be("bug");

        var update = await _factory.HttpClient.SendAsync(
            _factory.Authenticated(
                new HttpRequestMessage(HttpMethod.Patch, $"/api/tasks/{task.Id}")
                {
                    Content = JsonContent.Create(new
                    {
                        status = "InProgress",
                        tagIds = new[] { tag1.Id, tag2.Id }
                    })
                },
                owner.AccessToken));
        update.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = (await update.Content.ReadFromJsonAsync<TaskResponse>(DevHubApiFactory.ApiJsonOptions))!;
        updated.Status.Should().Be(DevHub.Domain.Enums.ProjectTaskStatus.InProgress);
        updated.Tags.Should().HaveCount(2);

        var comment = await _factory.HttpClient.PostAsJsonAsync(
            $"/api/tasks/{task.Id}/comments",
            new { content = "Let's start with the refresh endpoint" });
        comment.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var commentAccepted = await _factory.HttpClient.SendAsync(
            _factory.Authenticated(
                new HttpRequestMessage(HttpMethod.Post, $"/api/tasks/{task.Id}/comments")
                {
                    Content = JsonContent.Create(new { content = "Let's start with the refresh endpoint" })
                },
                owner.AccessToken));
        commentAccepted.StatusCode.Should().Be(HttpStatusCode.Created);
        var createdComment = (await commentAccepted.Content.ReadFromJsonAsync<CommentResponse>(DevHubApiFactory.ApiJsonOptions))!;
        createdComment.AuthorName.Should().Be("task-owner");

        var dashboard = await _factory.HttpClient.SendAsync(
            _factory.Authenticated(new HttpRequestMessage(HttpMethod.Get, $"/api/projects/{project.Id}/dashboard"), owner.AccessToken));
        dashboard.StatusCode.Should().Be(HttpStatusCode.OK);
        var dashboardData = (await dashboard.Content.ReadFromJsonAsync<ProjectDashboardResponse>(DevHubApiFactory.ApiJsonOptions))!;
        dashboardData.TotalTasks.Should().Be(1);
        dashboardData.TotalMembers.Should().Be(1);

        var activities = await _factory.HttpClient.SendAsync(
            _factory.Authenticated(new HttpRequestMessage(HttpMethod.Get, $"/api/projects/{project.Id}/activities"), owner.AccessToken));
        activities.StatusCode.Should().Be(HttpStatusCode.OK);
        var activityPage = (await activities.Content.ReadFromJsonAsync<DevHub.Application.Common.PagedResult<ActivityResponse>>(DevHubApiFactory.ApiJsonOptions))!;
        activityPage.Items.Should().Contain(a => a.Type == DevHub.Domain.Enums.ActivityType.TaskCreated);
    }

    [Fact]
    public async Task Task_cannot_be_assigned_to_non_member()
    {
        var owner = await _factory.RegisterAsync("task-non-member@test.com");
        var outsider = await _factory.RegisterAsync("task-outsider@test.com");
        var project = await _factory.CreateProjectAsync(owner.AccessToken, "Assignment project");

        var response = await _factory.HttpClient.SendAsync(
            _factory.Authenticated(
                new HttpRequestMessage(HttpMethod.Post, $"/api/projects/{project.Id}/tasks")
                {
                    Content = JsonContent.Create(new
                    {
                        title = "Assigned to outsider",
                        assigneeId = outsider.User.Id
                    })
                },
                owner.AccessToken));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task List_tasks_supports_filtering_and_pagination()
    {
        var owner = await _factory.RegisterAsync("task-filter@test.com");
        var project = await _factory.CreateProjectAsync(owner.AccessToken, "Filter project");

        for (var i = 0; i < 3; i++)
        {
            await _factory.CreateTaskAsync(owner.AccessToken, project.Id, $"Task {i}");
        }

        var list = await _factory.HttpClient.SendAsync(
            _factory.Authenticated(
                new HttpRequestMessage(HttpMethod.Get, $"/api/projects/{project.Id}/tasks?status=Backlog&page=1&pageSize=2"),
                owner.AccessToken));

        list.StatusCode.Should().Be(HttpStatusCode.OK);
        var page = (await list.Content.ReadFromJsonAsync<DevHub.Application.Common.PagedResult<TaskResponse>>(DevHubApiFactory.ApiJsonOptions))!;
        page.Items.Should().HaveCount(2);
        page.TotalItems.Should().Be(3);
        page.TotalPages.Should().Be(2);
    }

    private async Task<TagResponse> CreateTagAsync(string accessToken, Guid projectId, string name, string color)
    {
        var response = await _factory.HttpClient.SendAsync(
            _factory.Authenticated(
                new HttpRequestMessage(HttpMethod.Post, $"/api/projects/{projectId}/tags")
                {
                    Content = JsonContent.Create(new { name, color })
                },
                accessToken));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<TagResponse>(DevHubApiFactory.ApiJsonOptions))!;
    }
}
