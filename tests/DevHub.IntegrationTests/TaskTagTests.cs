using System.Net;
using System.Net.Http.Json;
using DevHub.Application.DTOs.Tags;
using DevHub.Application.DTOs.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DevHub.IntegrationTests;

/// <summary>
/// Covers the task/tag association, which is rewritten in full on every task update. The rows are
/// deleted and re-inserted, so the interesting cases are the ones where the resulting set is
/// identical to the previous one or a subset of it.
/// </summary>
public sealed class TaskTagTests : IClassFixture<DevHubApiFactory>
{
    private readonly DevHubApiFactory _factory;

    public TaskTagTests(DevHubApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Keeping_the_same_tag_on_update_keeps_it_attached()
    {
        var owner = await _factory.RegisterAsync("task-tag-same@test.com");
        var project = await _factory.CreateProjectAsync(owner.AccessToken, "Same tag project");
        var tag = await CreateTagAsync(owner.AccessToken, project.Id, "same-tag", "#ff0000");
        var task = await _factory.CreateTaskAsync(owner.AccessToken, project.Id, "Task with a tag");

        var withTag = await PatchTaskAsync(owner.AccessToken, task.Id, new { tagIds = new[] { tag.Id } });
        withTag.StatusCode.Should().Be(HttpStatusCode.OK);

        // Sending the very same tag again rewrites the association: delete all, re-insert the same set.
        var sameTag = await PatchTaskAsync(owner.AccessToken, task.Id, new { tagIds = new[] { tag.Id } });
        sameTag.StatusCode.Should().Be(HttpStatusCode.OK);
        var response = (await sameTag.Content.ReadFromJsonAsync<TaskResponse>(DevHubApiFactory.ApiJsonOptions))!;
        response.Tags.Should().ContainSingle(t => t.Id == tag.Id);

        var reloaded = await _factory.HttpClient.SendAsync(
            _factory.Authenticated(new HttpRequestMessage(HttpMethod.Get, $"/api/tasks/{task.Id}"), owner.AccessToken));
        reloaded.StatusCode.Should().Be(HttpStatusCode.OK);
        var reloadedTask = (await reloaded.Content.ReadFromJsonAsync<TaskResponse>(DevHubApiFactory.ApiJsonOptions))!;
        reloadedTask.Tags.Should().ContainSingle(t => t.Id == tag.Id);
    }

    [Fact]
    public async Task Dropping_a_tag_on_update_detaches_it()
    {
        var owner = await _factory.RegisterAsync("task-tag-drop@test.com");
        var project = await _factory.CreateProjectAsync(owner.AccessToken, "Drop tag project");
        var kept = await CreateTagAsync(owner.AccessToken, project.Id, "kept-tag", "#00ff00");
        var dropped = await CreateTagAsync(owner.AccessToken, project.Id, "dropped-tag", "#0000ff");
        var task = await _factory.CreateTaskAsync(owner.AccessToken, project.Id, "Task with two tags");

        (await PatchTaskAsync(owner.AccessToken, task.Id, new { tagIds = new[] { kept.Id, dropped.Id } }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await PatchTaskAsync(owner.AccessToken, task.Id, new { tagIds = new[] { kept.Id } });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = (await response.Content.ReadFromJsonAsync<TaskResponse>(DevHubApiFactory.ApiJsonOptions))!;
        updated.Tags.Should().ContainSingle(t => t.Id == kept.Id);
    }

    [Fact]
    public async Task Clearing_the_tag_list_on_update_detaches_every_tag()
    {
        var owner = await _factory.RegisterAsync("task-tag-clear@test.com");
        var project = await _factory.CreateProjectAsync(owner.AccessToken, "Clear tag project");
        var tag = await CreateTagAsync(owner.AccessToken, project.Id, "clear-tag", "#123456");
        var task = await _factory.CreateTaskAsync(owner.AccessToken, project.Id, "Task to clear");

        (await PatchTaskAsync(owner.AccessToken, task.Id, new { tagIds = new[] { tag.Id } }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await PatchTaskAsync(owner.AccessToken, task.Id, new { tagIds = Array.Empty<Guid>() });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = (await response.Content.ReadFromJsonAsync<TaskResponse>(DevHubApiFactory.ApiJsonOptions))!;
        updated.Tags.Should().BeEmpty();
    }

    [Fact]
    public async Task Tagging_a_task_attaches_a_global_tag_to_the_project()
    {
        var owner = await _factory.RegisterAsync("task-tag-global@test.com");
        var project = await _factory.CreateProjectAsync(owner.AccessToken, "Global tag project");
        var task = await _factory.CreateTaskAsync(owner.AccessToken, project.Id, "Task using a global tag");

        // "bug" is part of the seeded global taxonomy, shared by every project.
        var response = await PatchTaskAsync(owner.AccessToken, task.Id, new { tagIds = new[] { await GlobalTagIdAsync("bug") } });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = (await response.Content.ReadFromJsonAsync<TaskResponse>(DevHubApiFactory.ApiJsonOptions))!;
        updated.Tags.Should().ContainSingle(t => t.Name == "bug");

        var tags = await _factory.HttpClient.SendAsync(
            _factory.Authenticated(new HttpRequestMessage(HttpMethod.Get, $"/api/projects/{project.Id}/tags"), owner.AccessToken));
        tags.StatusCode.Should().Be(HttpStatusCode.OK);
        var projectTags = (await tags.Content.ReadFromJsonAsync<IReadOnlyList<TagResponse>>(DevHubApiFactory.ApiJsonOptions))!;
        projectTags.Should().Contain(t => t.Name == "bug");
    }

    [Fact]
    public async Task Updating_a_task_without_touching_tagIds_keeps_the_tags()
    {
        var owner = await _factory.RegisterAsync("task-tag-untouched@test.com");
        var project = await _factory.CreateProjectAsync(owner.AccessToken, "Untouched tag project");
        var tag = await CreateTagAsync(owner.AccessToken, project.Id, "keep-me", "#abcdef");
        var task = await _factory.CreateTaskAsync(owner.AccessToken, project.Id, "Task whose labels must survive");

        (await PatchTaskAsync(owner.AccessToken, task.Id, new { tagIds = new[] { tag.Id } }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        // "tagIds" absent means the caller said nothing about tags, so an unrelated edit - here a
        // status change - must not drop them.
        var response = await PatchTaskAsync(owner.AccessToken, task.Id, new { status = "InProgress" });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = (await response.Content.ReadFromJsonAsync<TaskResponse>(DevHubApiFactory.ApiJsonOptions))!;
        updated.Status.Should().Be(DevHub.Domain.Enums.ProjectTaskStatus.InProgress);
        updated.Tags.Should().ContainSingle(t => t.Id == tag.Id);
    }

    [Fact]
    public async Task Referencing_an_unknown_tag_is_rejected()
    {
        var owner = await _factory.RegisterAsync("task-tag-unknown@test.com");
        var project = await _factory.CreateProjectAsync(owner.AccessToken, "Unknown tag project");
        var task = await _factory.CreateTaskAsync(owner.AccessToken, project.Id, "Task with a bad tag");

        var response = await PatchTaskAsync(owner.AccessToken, task.Id, new { tagIds = new[] { Guid.NewGuid() } });
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private Task<HttpResponseMessage> PatchTaskAsync(string accessToken, Guid taskId, object body)
        => _factory.HttpClient.SendAsync(
            _factory.Authenticated(
                new HttpRequestMessage(HttpMethod.Patch, $"/api/tasks/{taskId}")
                {
                    Content = JsonContent.Create(body)
                },
                accessToken));

    private async Task<Guid> GlobalTagIdAsync(string name)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<DevHub.Infrastructure.Persistence.ApplicationDbContext>();
        return await context.Tags
            .Where(t => t.ProjectId == null && t.Name == name)
            .Select(t => t.Id)
            .SingleAsync();
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
