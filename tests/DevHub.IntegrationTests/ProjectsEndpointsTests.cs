using System.Net;
using System.Net.Http.Json;
using DevHub.Application.DTOs.Auth;
using DevHub.Application.DTOs.Projects;
using DevHub.Application.DTOs.Users;
using FluentAssertions;

namespace DevHub.IntegrationTests;

public sealed class ProjectsEndpointsTests : IClassFixture<DevHubApiFactory>
{
    private readonly DevHubApiFactory _factory;

    public ProjectsEndpointsTests(DevHubApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Create_project_sets_creator_as_owner()
    {
        var auth = await _factory.RegisterAsync("project-owner@test.com");
        var project = await _factory.CreateProjectAsync(auth.AccessToken, "Owned project");

        project.Name.Should().Be("Owned project");
        project.OwnerId.Should().Be(auth.User.Id);
        project.OwnerName.Should().Be("project-owner");
        project.Status.Should().Be(DevHub.Domain.Enums.ProjectStatus.Active);
        project.MemberCount.Should().Be(1);
        project.TaskCount.Should().Be(0);
    }

    [Fact]
    public async Task Create_project_points_at_the_created_resource()
    {
        var auth = await _factory.RegisterAsync("project-location@test.com");

        var request = _factory.Authenticated(
            new HttpRequestMessage(HttpMethod.Post, "/api/projects")
            {
                Content = JsonContent.Create(new { name = "Locate me" })
            },
            auth.AccessToken);

        var response = await _factory.HttpClient.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var created = await response.Content.ReadFromJsonAsync<ProjectResponse>(DevHubApiFactory.ApiJsonOptions);
        var location = response.Headers.Location;
        location.Should().NotBeNull("a 201 has to say where the new project lives");
        location!.AbsolutePath.Should().Be($"/api/projects/{created!.Id}");

        // And the URL it advertises has to actually serve the project, on a GET.
        var followed = await _factory.HttpClient.SendAsync(
            _factory.Authenticated(new HttpRequestMessage(HttpMethod.Get, location), auth.AccessToken));

        followed.StatusCode.Should().Be(HttpStatusCode.OK);
        (await followed.Content.ReadFromJsonAsync<ProjectResponse>(DevHubApiFactory.ApiJsonOptions))!
            .Id.Should().Be(created.Id);
    }

    [Fact]
    public async Task Register_points_at_a_location_that_can_actually_be_fetched()
    {
        var response = await _factory.HttpClient.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/api/auth/register")
        {
            Content = JsonContent.Create(new { email = "register-location@test.com", password = "Str0ng!Pass123", displayName = "Location" })
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var location = response.Headers.Location;
        location.Should().NotBeNull("a 201 has to say where the created account lives");

        // Location used to name this very POST route, which answers 405 to a GET - so any client that
        // followed the header broke. It must name the profile the caller can read back with a GET.
        location!.AbsolutePath.Should().NotBe("/api/auth/register");
        var followed = await _factory.HttpClient.SendAsync(
            _factory.Authenticated(new HttpRequestMessage(HttpMethod.Get, location), (await response.Content.ReadFromJsonAsync<AuthResponse>(DevHubApiFactory.ApiJsonOptions))!.AccessToken));

        followed.StatusCode.Should().Be(HttpStatusCode.OK);
        (await followed.Content.ReadFromJsonAsync<UserResponse>(DevHubApiFactory.ApiJsonOptions))!
            .Email.Should().Be("register-location@test.com");
    }

    [Fact]
    public async Task Archived_project_cannot_be_created()
    {
        var auth = await _factory.RegisterAsync("archived-proj@test.com");

        var request = _factory.Authenticated(
            new HttpRequestMessage(HttpMethod.Post, "/api/projects")
            {
                Content = JsonContent.Create(new { name = "Archive me", status = "Archived" })
            },
            auth.AccessToken);

        var response = await _factory.HttpClient.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Owner_can_get_list_update_and_delete_project()
    {
        var auth = await _factory.RegisterAsync("project-crud@test.com");
        var project = await _factory.CreateProjectAsync(auth.AccessToken, "CRUD project");

        var listRequest = _factory.Authenticated(new HttpRequestMessage(HttpMethod.Get, "/api/projects"), auth.AccessToken);
        var list = await _factory.HttpClient.SendAsync(listRequest);
        list.StatusCode.Should().Be(HttpStatusCode.OK);
        (await list.Content.ReadFromJsonAsync<DevHub.Application.Common.PagedResult<ProjectSummaryResponse>>(DevHubApiFactory.ApiJsonOptions))
            !.Items.Should().Contain(i => i.Id == project.Id);

        var getRequest = _factory.Authenticated(new HttpRequestMessage(HttpMethod.Get, $"/api/projects/{project.Id}"), auth.AccessToken);
        var get = await _factory.HttpClient.SendAsync(getRequest);
        get.StatusCode.Should().Be(HttpStatusCode.OK);
        (await get.Content.ReadFromJsonAsync<ProjectResponse>(DevHubApiFactory.ApiJsonOptions))!.Name.Should().Be("CRUD project");

        var updateRequest = _factory.Authenticated(
            new HttpRequestMessage(HttpMethod.Patch, $"/api/projects/{project.Id}")
            {
                Content = JsonContent.Create(new { description = "Updated description" })
            },
            auth.AccessToken);
        var updated = await _factory.HttpClient.SendAsync(updateRequest);
        updated.StatusCode.Should().Be(HttpStatusCode.OK);
        (await updated.Content.ReadFromJsonAsync<ProjectResponse>(DevHubApiFactory.ApiJsonOptions))!.Description.Should().Be("Updated description");

        var deleteRequest = _factory.Authenticated(new HttpRequestMessage(HttpMethod.Delete, $"/api/projects/{project.Id}"), auth.AccessToken);
        var deleted = await _factory.HttpClient.SendAsync(deleteRequest);
        deleted.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var afterDelete = await _factory.HttpClient.SendAsync(
            _factory.Authenticated(new HttpRequestMessage(HttpMethod.Get, $"/api/projects/{project.Id}"), auth.AccessToken));
        afterDelete.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Non_member_cannot_access_project()
    {
        var owner = await _factory.RegisterAsync("owner-access@test.com");
        var outsider = await _factory.RegisterAsync("outsider-access@test.com");
        var project = await _factory.CreateProjectAsync(owner.AccessToken, "Private project");

        var getRequest = _factory.Authenticated(new HttpRequestMessage(HttpMethod.Get, $"/api/projects/{project.Id}"), outsider.AccessToken);
        var response = await _factory.HttpClient.SendAsync(getRequest);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Viewer_cannot_manage_members()
    {
        var owner = await _factory.RegisterAsync("owner-members@test.com");
        var viewer = await _factory.RegisterAsync("viewer-members@test.com");
        var project = await _factory.CreateProjectAsync(owner.AccessToken, "Member project");

        var addViewer = await _factory.HttpClient.SendAsync(
            _factory.Authenticated(
                new HttpRequestMessage(HttpMethod.Post, $"/api/projects/{project.Id}/members")
                {
                    Content = JsonContent.Create(new { email = "viewer-members@test.com", role = "Viewer" })
                },
                owner.AccessToken));
        addViewer.StatusCode.Should().Be(HttpStatusCode.Created);

        var addMember = await _factory.HttpClient.SendAsync(
            _factory.Authenticated(
                new HttpRequestMessage(HttpMethod.Post, $"/api/projects/{project.Id}/members")
                {
                    Content = JsonContent.Create(new { email = "another@test.com", role = "Member" })
                },
                viewer.AccessToken));

        addMember.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
