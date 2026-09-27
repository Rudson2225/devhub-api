using System.Net;
using System.Net.Http.Json;
using DevHub.Application.DTOs.Tags;
using DevHub.Domain.Entities;
using DevHub.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace DevHub.IntegrationTests;

/// <summary>
/// Tag uniqueness is scoped, not global: the global taxonomy (ProjectId IS NULL) and every project
/// scope are independent. These tests also pin the cross-tenant isolation of shared global tags.
/// </summary>
public sealed class TagScopeUniquenessTests : IClassFixture<DevHubApiFactory>
{
    private readonly DevHubApiFactory _factory;

    public TagScopeUniquenessTests(DevHubApiFactory factory) => _factory = factory;

    private async Task<HttpResponseMessage> CreateTagAsync(string token, Guid projectId, string name, string? color = null, string? description = null)
        => await _factory.HttpClient.SendAsync(_factory.Authenticated(
            new HttpRequestMessage(HttpMethod.Post, $"/api/projects/{projectId}/tags")
            {
                Content = JsonContent.Create(new { name, color, description })
            }, token));

    private async Task<Guid> TagIdAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<TagResponse>(DevHubApiFactory.ApiJsonOptions))!.Id;
    }

    [Fact]
    public async Task Global_tag_and_same_named_project_tag_can_coexist()
    {
        var owner = await _factory.RegisterAsync($"tag-scope-global-{Guid.NewGuid():N}@test.com");
        var project = await _factory.CreateProjectAsync(owner.AccessToken, "Scope global vs project");
        var name = $"coexist-{Guid.NewGuid():N}";

        // A project-scoped row created before any global tag with that name exists.
        var projectScoped = await CreateTagAsync(owner.AccessToken, project.Id, name);

        // Now a global tag with the same name is inserted directly, emulating the platform taxonomy.
        var globalId = await InsertGlobalTagAsync(name);

        projectScoped.StatusCode.Should().Be(HttpStatusCode.Created);
        globalId.Should().NotBe(Guid.Empty);

        var projectScopedTag = await LoadTagAsync(await TagIdAsync(projectScoped));
        projectScopedTag.ProjectId.Should().Be(project.Id);
        (await LoadTagAsync(globalId)).ProjectId.Should().BeNull("the platform tag stays global");
    }

    [Fact]
    public async Task Two_projects_can_each_own_the_same_tag_name()
    {
        var owner = await _factory.RegisterAsync($"tag-scope-two-{Guid.NewGuid():N}@test.com");
        var projectA = await _factory.CreateProjectAsync(owner.AccessToken, "Scope project A");
        var projectB = await _factory.CreateProjectAsync(owner.AccessToken, "Scope project B");

        var responseA = await CreateTagAsync(owner.AccessToken, projectA.Id, "release-blocker");
        var responseB = await CreateTagAsync(owner.AccessToken, projectB.Id, "release-blocker");

        // Before this fix the second request produced a 500 from the globally-unique name index.
        responseA.StatusCode.Should().Be(HttpStatusCode.Created);
        responseB.StatusCode.Should().Be(HttpStatusCode.Created);

        var tagA = await TagIdAsync(responseA);
        var tagB = await TagIdAsync(responseB);
        tagA.Should().NotBe(tagB, "each project owns its own tag row");

        await AssertTagRowAsync(tagA, projectA.Id, "release-blocker");
        await AssertTagRowAsync(tagB, projectB.Id, "release-blocker");
    }

    [Fact]
    public async Task Duplicate_tag_within_the_same_project_is_rejected()
    {
        var owner = await _factory.RegisterAsync($"tag-scope-dup-{Guid.NewGuid():N}@test.com");
        var project = await _factory.CreateProjectAsync(owner.AccessToken, "Scope duplicate");

        (await CreateTagAsync(owner.AccessToken, project.Id, "unique-in-project")).StatusCode
            .Should().Be(HttpStatusCode.Created);

        var duplicate = await CreateTagAsync(owner.AccessToken, project.Id, "unique-in-project");

        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await duplicate.Content.ReadAsStringAsync()).Should().Contain("conflict");
    }

    [Fact]
    public async Task Database_rejects_duplicate_tag_in_the_same_project_scope()
    {
        var owner = await _factory.RegisterAsync($"tag-scope-db-dup-{Guid.NewGuid():N}@test.com");
        var project = await _factory.CreateProjectAsync(owner.AccessToken, "Scope db duplicate");
        var name = $"db-dup-{Guid.NewGuid():N}";

        (await CreateTagAsync(owner.AccessToken, project.Id, name)).StatusCode.Should().Be(HttpStatusCode.Created);

        // Bypasses the service-level pre-check to prove the database constraint is the real guard.
        var act = async () =>
        {
            using var scope = _factory.Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            context.Tags.Add(new Tag { ProjectId = project.Id, Name = name, Color = "#000000" });
            await context.SaveChangesAsync();
        };

        var thrown = await act.Should().ThrowAsync<DbUpdateException>();
        thrown.Which.InnerException.Should().BeOfType<PostgresException>()
            .Which.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
    }

    [Fact]
    public async Task Database_rejects_duplicate_global_tag()
    {
        var name = $"global-dup-{Guid.NewGuid():N}";
        await InsertGlobalTagAsync(name);

        var act = async () =>
        {
            using var scope = _factory.Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            context.Tags.Add(new Tag { ProjectId = null, Name = name, Color = "#000000" });
            await context.SaveChangesAsync();
        };

        var thrown = await act.Should().ThrowAsync<DbUpdateException>();
        thrown.Which.InnerException.Should().BeOfType<PostgresException>()
            .Which.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
    }

    [Fact]
    public async Task Concurrent_creation_of_the_same_tag_never_surfaces_as_500()
    {
        var owner = await _factory.RegisterAsync($"tag-scope-race-{Guid.NewGuid():N}@test.com");
        var project = await _factory.CreateProjectAsync(owner.AccessToken, "Scope race");
        var name = $"raced-{Guid.NewGuid():N}";

        // Parallel requests all pass the service pre-check, so the database constraint is what
        // resolves the race. Losing requests must surface as 409, never as an unhandled 500.
        var responses = await Task.WhenAll(
            Enumerable.Range(0, 8).Select(_ => CreateTagAsync(owner.AccessToken, project.Id, name)));

        responses.Should().OnlyContain(r => r.StatusCode != HttpStatusCode.InternalServerError);
        responses.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(1);
        responses.Count(r => r.StatusCode == HttpStatusCode.Conflict).Should().Be(7);
    }

    [Fact]
    public async Task Reusing_a_global_tag_from_one_project_does_not_mutate_it_for_others()
    {
        var globalName = $"shared-{Guid.NewGuid():N}";
        var globalId = await InsertGlobalTagAsync(globalName, "#111111", "original global description");

        var projectAActor = await _factory.RegisterAsync($"tag-iso-a-{Guid.NewGuid():N}@test.com");
        var projectBActor = await _factory.RegisterAsync($"tag-iso-b-{Guid.NewGuid():N}@test.com");
        var projectA = await _factory.CreateProjectAsync(projectAActor.AccessToken, "Isolation A");
        var projectB = await _factory.CreateProjectAsync(projectBActor.AccessToken, "Isolation B");

        // Project A admin tries to hijack the shared global tag while attaching it.
        var hijack = await _factory.HttpClient.SendAsync(_factory.Authenticated(
            new HttpRequestMessage(HttpMethod.Post, $"/api/projects/{projectA.Id}/tags")
            {
                Content = JsonContent.Create(new { name = globalName, color = "#ff0000", description = "hijacked by project A" })
            }, projectAActor.AccessToken));
        hijack.StatusCode.Should().Be(HttpStatusCode.Created);

        // Project B then attaches the very same tag and must observe the untouched global values.
        var attachB = await _factory.HttpClient.SendAsync(_factory.Authenticated(
            new HttpRequestMessage(HttpMethod.Post, $"/api/projects/{projectB.Id}/tags")
            {
                Content = JsonContent.Create(new { name = globalName, color = "#00ff00", description = "hijacked by project B" })
            }, projectBActor.AccessToken));
        attachB.StatusCode.Should().Be(HttpStatusCode.Created);

        var observedByB = await attachB.Content.ReadFromJsonAsync<TagResponse>(DevHubApiFactory.ApiJsonOptions);
        observedByB!.Id.Should().Be(globalId);
        observedByB.Color.Should().Be("#111111", "project A must not have rewritten the shared global colour");
        observedByB.Description.Should().Be("original global description");

        // And the stored row itself is unchanged.
        var stored = await LoadTagAsync(globalId);
        stored.Color.Should().Be("#111111");
        stored.Description.Should().Be("original global description");

        // The tag is genuinely associated with both projects.
        (await _factory.HttpClient.SendAsync(_factory.Authenticated(
            new HttpRequestMessage(HttpMethod.Get, $"/api/projects/{projectA.Id}/tags"), projectAActor.AccessToken)))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await _factory.HttpClient.SendAsync(_factory.Authenticated(
            new HttpRequestMessage(HttpMethod.Get, $"/api/projects/{projectB.Id}/tags"), projectBActor.AccessToken)))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Global_tag_cannot_be_renamed_through_a_project()
    {
        var globalName = $"immutable-{Guid.NewGuid():N}";
        var globalId = await InsertGlobalTagAsync(globalName, "#222222");

        var actor = await _factory.RegisterAsync($"tag-immutable-{Guid.NewGuid():N}@test.com");
        var project = await _factory.CreateProjectAsync(actor.AccessToken, "Immutable global");

        (await CreateTagAsync(actor.AccessToken, project.Id, globalName)).StatusCode
            .Should().Be(HttpStatusCode.Created);

        var update = await _factory.HttpClient.SendAsync(_factory.Authenticated(
            new HttpRequestMessage(HttpMethod.Patch, $"/api/projects/{project.Id}/tags/{globalId}")
            {
                Content = JsonContent.Create(new { name = "renamed-by-admin", color = "#000000" })
            }, actor.AccessToken));

        update.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var stored = await LoadTagAsync(globalId);
        stored.Name.Should().Be(globalName);
        stored.Color.Should().Be("#222222");
    }

    [Fact]
    public async Task Updating_a_tag_that_belongs_to_another_project_is_a_404_not_a_400()
    {
        var owner = await _factory.RegisterAsync($"tag-scope-foreign-{Guid.NewGuid():N}@test.com");
        var mine = await _factory.CreateProjectAsync(owner.AccessToken, "Project that owns the tag");
        var theirs = await _factory.CreateProjectAsync(owner.AccessToken, "Project that must stay out");

        var tagId = await TagIdAsync(await CreateTagAsync(owner.AccessToken, mine.Id, "owned-elsewhere"));

        var response = await _factory.HttpClient.SendAsync(_factory.Authenticated(
            new HttpRequestMessage(HttpMethod.Patch, $"/api/projects/{theirs.Id}/tags/{tagId}")
            {
                Content = JsonContent.Create(new { name = "renamed-by-a-stranger" })
            }, owner.AccessToken));

        // The caller may manage tags in the second project only. The tag lives in the first, so the
        // answer must be the same one an id that does not exist gets - 404 - and must not explain
        // itself with "this tag is a global tag", which is a different situation entirely.
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain("global tag");
        body.Should().NotContain("renamed-by-a-stranger");

        (await LoadTagAsync(tagId)).Name.Should().Be("owned-elsewhere", "the tag was not renamed");
    }

    [Fact]
    public async Task Updating_a_global_tag_through_a_project_stays_a_400_and_says_why()
    {
        var globalName = $"shared-rename-{Guid.NewGuid():N}";
        var globalId = await InsertGlobalTagAsync(globalName, "#111111", "original global description");

        var actor = await _factory.RegisterAsync($"tag-rename-global-{Guid.NewGuid():N}@test.com");
        var project = await _factory.CreateProjectAsync(actor.AccessToken, "Project holding the global tag");

        // Associating it to the project is allowed and is what the taxonomy is for.
        var attach = await CreateTagAsync(actor.AccessToken, project.Id, globalName);
        (await TagIdAsync(attach)).Should().Be(globalId);

        // Renaming it through the project is not: the row is shared with every other project, so the
        // request is refused as a bad request - not as a missing tag - and the reason is spelled out.
        var response = await _factory.HttpClient.SendAsync(_factory.Authenticated(
            new HttpRequestMessage(HttpMethod.Patch, $"/api/projects/{project.Id}/tags/{globalId}")
            {
                Content = JsonContent.Create(new { name = "renamed-through-a-project" })
            }, actor.AccessToken));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("global tag");
        (await LoadTagAsync(globalId)).Name.Should().Be(globalName, "the shared row is untouched");
    }

    private async Task<Guid> InsertGlobalTagAsync(string name, string color = "#6b7280", string? description = null)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var tag = new Tag { ProjectId = null, Name = name, Color = color, Description = description };
        context.Tags.Add(tag);
        await context.SaveChangesAsync();
        return tag.Id;
    }

    private async Task<Tag> LoadTagAsync(Guid id)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await context.Tags.AsNoTracking().FirstAsync(t => t.Id == id);
    }

    private async Task AssertTagRowAsync(Guid tagId, Guid projectId, string name)
    {
        var tag = await LoadTagAsync(tagId);
        tag.ProjectId.Should().Be(projectId);
        tag.Name.Should().Be(name);
    }
}
