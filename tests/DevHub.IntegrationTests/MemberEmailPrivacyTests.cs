using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace DevHub.IntegrationTests;

/// <summary>
/// Covers M8: the member roster is visible to everyone in the project, the email addresses behind it
/// are not. Only a caller holding members.manage receives them, so neither a Viewer nor a plain
/// Member can walk away with every address in the project.
/// </summary>
public sealed class MemberEmailPrivacyTests : IClassFixture<DevHubApiFactory>
{
    private sealed record Roster(
        Guid ProjectId,
        string OwnerEmail,
        string ViewerEmail,
        string MemberEmail,
        string OwnerToken,
        string ViewerToken,
        string MemberToken);

    private readonly DevHubApiFactory _factory;

    public MemberEmailPrivacyTests(DevHubApiFactory factory) => _factory = factory;

    private async Task<Roster> ArrangeAsync(string prefix)
    {
        var owner = await _factory.RegisterAsync($"{prefix}-owner@test.com");
        var viewer = await _factory.RegisterAsync($"{prefix}-viewer@test.com");
        var member = await _factory.RegisterAsync($"{prefix}-member@test.com");
        var project = await _factory.CreateProjectAsync(owner.AccessToken, $"Roster {prefix}");

        foreach (var (email, role) in new[] { (viewer.User.Email, "Viewer"), (member.User.Email, "Member") })
        {
            var add = await _factory.HttpClient.SendAsync(
                _factory.Authenticated(
                    new HttpRequestMessage(HttpMethod.Post, $"/api/projects/{project.Id}/members")
                    {
                        Content = JsonContent.Create(new { email, role })
                    },
                    owner.AccessToken));

            add.StatusCode.Should().Be(HttpStatusCode.Created);
        }

        return new Roster(
            project.Id,
            owner.User.Email,
            viewer.User.Email,
            member.User.Email,
            owner.AccessToken,
            viewer.AccessToken,
            member.AccessToken);
    }

    private async Task<List<(Guid UserId, string? Email, string DisplayName, string Role)>> ListAsync(
        Guid projectId,
        string token)
    {
        var response = await _factory.HttpClient.SendAsync(
            _factory.Authenticated(
                new HttpRequestMessage(HttpMethod.Get, $"/api/projects/{projectId}/members"),
                token));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.EnumerateArray()
            .Select(row => (
                row.GetProperty("userId").GetGuid(),
                row.GetProperty("email").GetString(),
                row.GetProperty("displayName").GetString() ?? "",
                row.GetProperty("role").GetString() ?? ""))
            .ToList();
    }

    [Fact]
    public async Task The_roster_still_identifies_who_is_who_without_addresses()
    {
        var roster = await ArrangeAsync("shape");

        var rows = await ListAsync(roster.ProjectId, roster.ViewerToken);

        rows.Should().HaveCount(3, "the roster itself is not restricted");
        rows.Select(r => r.Role).Should().BeEquivalentTo(["Owner", "Member", "Viewer"]);
        rows.Select(r => r.DisplayName).Should().OnlyContain(d => d.Length > 0,
            "display name is what collaborators actually need");
    }

    [Fact]
    public async Task Viewer_receives_only_their_own_address()
    {
        var roster = await ArrangeAsync("view");

        var rows = await ListAsync(roster.ProjectId, roster.ViewerToken);

        rows.Count(r => r.Email is not null).Should().Be(1,
            "a Viewer must not receive contact details of the other members");
        rows.Single(r => r.Email is not null).Email.Should().Be(roster.ViewerEmail,
            "the one address they do get is their own");
    }

    [Fact]
    public async Task Plain_member_also_gets_the_others_redacted()
    {
        var roster = await ArrangeAsync("plain");

        var rows = await ListAsync(roster.ProjectId, roster.MemberToken);

        rows.Count(r => r.Email is not null).Should().Be(1,
            "members.manage is what opens the addresses, and a Member does not hold it");
        rows.Single(r => r.Email is not null).Email.Should().Be(roster.MemberEmail);
    }

    [Fact]
    public async Task The_owner_holding_members_manage_still_sees_every_address()
    {
        var roster = await ArrangeAsync("owner");

        var rows = await ListAsync(roster.ProjectId, roster.OwnerToken);

        rows.Select(r => r.Email).Should().BeEquivalentTo(
            [roster.OwnerEmail, roster.ViewerEmail, roster.MemberEmail],
            "a manager needs the addresses to identify who they are administering");
    }
}
