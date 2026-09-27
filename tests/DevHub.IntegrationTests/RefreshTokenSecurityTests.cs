using System.Net;
using System.Net.Http.Json;
using DevHub.Application.DTOs.Auth;
using DevHub.Domain.Entities;
using DevHub.Infrastructure.Authentication;
using DevHub.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DevHub.IntegrationTests;

/// <summary>
/// Refresh token rotation must be single-use, and replaying an already-rotated token is treated as
/// evidence that the chain leaked: every session of the affected account is revoked and the caller
/// only ever sees a generic error.
/// </summary>
public sealed class RefreshTokenSecurityTests : IClassFixture<DevHubApiFactory>
{
    private const string GenericRefreshFailure = "Invalid refresh token.";

    private readonly DevHubApiFactory _factory;

    public RefreshTokenSecurityTests(DevHubApiFactory factory) => _factory = factory;

    private async Task<HttpResponseMessage> RefreshAsync(string refreshToken, string? accessToken = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh")
        {
            Content = JsonContent.Create(new { refreshToken })
        };

        if (accessToken is not null)
        {
            _factory.Authenticated(request, accessToken);
        }

        return await _factory.HttpClient.SendAsync(request);
    }

    private async Task<AuthResponse> ReadAuthAsync(HttpResponseMessage response)
        => (await response.Content.ReadFromJsonAsync<AuthResponse>(DevHubApiFactory.ApiJsonOptions))!;

    private async Task<RefreshToken> LoadTokenAsync(string plainToken)
    {
        var hash = new RefreshTokenGenerator().Hash(plainToken);
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await context.RefreshTokens.AsNoTracking().FirstAsync(t => t.TokenHash == hash);
    }

    private async Task<List<RefreshToken>> LoadAllTokensForUserAsync(Guid userId)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await context.RefreshTokens.AsNoTracking().Where(t => t.UserId == userId).ToListAsync();
    }

    [Fact]
    public async Task Login_issues_a_refresh_token_in_a_new_family()
    {
        var user = await _factory.RegisterAsync($"rt-login-{Guid.NewGuid():N}@test.com");

        var stored = await LoadTokenAsync(user.RefreshToken);

        stored.FamilyId.Should().NotBe(Guid.Empty);
        stored.IsUsed.Should().BeFalse();
        stored.RevokedAt.Should().BeNull();
    }

    [Fact]
    public async Task Refresh_rotates_the_token_and_keeps_it_inside_the_same_family()
    {
        var user = await _factory.RegisterAsync($"rt-rotate-{Guid.NewGuid():N}@test.com");
        var first = await LoadTokenAsync(user.RefreshToken);

        var response = await RefreshAsync(user.RefreshToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var rotated = await ReadAuthAsync(response);

        var oldToken = await LoadTokenAsync(user.RefreshToken);
        var newToken = await LoadTokenAsync(rotated.RefreshToken);

        oldToken.IsUsed.Should().BeTrue("the presented token is consumed by the rotation");
        oldToken.RevokedAt.Should().NotBeNull();
        oldToken.ReplacedByTokenHash.Should().Be(newToken.TokenHash);
        newToken.IsUsed.Should().BeFalse();
        newToken.FamilyId.Should().Be(first.FamilyId, "a rotation inherits the session family");
        rotated.RefreshToken.Should().NotBe(user.RefreshToken);
    }

    [Fact]
    public async Task Replaying_a_rotated_token_revokes_every_session_of_the_account()
    {
        var victim = await _factory.RegisterAsync($"rt-victim-{Guid.NewGuid():N}@test.com");

        // A second, completely independent session for the same account.
        var login = await _factory.HttpClient.PostAsJsonAsync("/api/auth/login", new
        {
            email = victim.User.Email,
            password = "Str0ng!Pass123"
        });
        login.StatusCode.Should().Be(HttpStatusCode.OK);
        var secondSession = await ReadAuthAsync(login);

        var originalFamily = (await LoadTokenAsync(victim.RefreshToken)).FamilyId;
        (await LoadTokenAsync(secondSession.RefreshToken)).FamilyId
            .Should().NotBe(originalFamily, "each login starts its own session family");

        var rotation = await RefreshAsync(victim.RefreshToken);
        rotation.StatusCode.Should().Be(HttpStatusCode.OK);
        var rotated = await ReadAuthAsync(rotation);

        // The attacker replays the token that was already rotated.
        var replay = await RefreshAsync(victim.RefreshToken);

        replay.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await replay.Content.ReadAsStringAsync()).Should().Contain(GenericRefreshFailure);

        // Every token of the account, in every session, is now dead.
        var all = await LoadAllTokensForUserAsync(victim.User.Id);
        all.Should().NotBeEmpty();
        all.Should().OnlyContain(t => t.RevokedAt != null, "Option A revokes all sessions of the user");
        all.Should().OnlyContain(t => t.UserId == victim.User.Id);

        // Including the rotated token that was still valid a moment ago.
        (await LoadTokenAsync(rotated.RefreshToken)).RevokedAt.Should().NotBeNull();
        (await LoadTokenAsync(secondSession.RefreshToken)).RevokedAt.Should().NotBeNull();

        // And a fresh refresh attempt on the compromised chain is refused.
        var afterCompromise = await RefreshAsync(rotated.RefreshToken);
        afterCompromise.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await afterCompromise.Content.ReadAsStringAsync()).Should().Contain(GenericRefreshFailure);
    }

    [Fact]
    public async Task Replay_does_not_leak_why_the_token_was_rejected()
    {
        var user = await _factory.RegisterAsync($"rt-generic-{Guid.NewGuid():N}@test.com");

        var unknown = await RefreshAsync("this-token-was-never-issued");
        var rotation = await RefreshAsync(user.RefreshToken);
        rotation.StatusCode.Should().Be(HttpStatusCode.OK);
        var rotated = await ReadAuthAsync(rotation);
        var replayed = await RefreshAsync(user.RefreshToken);

        var unknownBody = await unknown.Content.ReadAsStringAsync();
        var replayedBody = await replayed.Content.ReadAsStringAsync();

        unknown.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        replayed.StatusCode.Should().Be(unknown.StatusCode);
        (await ProblemBody.WithoutTraceIdAsync(replayedBody))
            .Should().Be(await ProblemBody.WithoutTraceIdAsync(unknownBody),
                "an attacker must not learn whether the token existed");

        foreach (var body in new[] { unknownBody, replayedBody })
        {
            body.Should().NotContain("reuse").And.NotContain("revoked").And.NotContain("expired")
                .And.NotContain("used").And.NotContain("family");
        }

        rotated.RefreshToken.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Reuse_detection_does_not_lock_out_a_different_account()
    {
        var victim = await _factory.RegisterAsync($"rt-isolated-a-{Guid.NewGuid():N}@test.com");
        var bystander = await _factory.RegisterAsync($"rt-isolated-b-{Guid.NewGuid():N}@test.com");

        (await RefreshAsync(victim.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await RefreshAsync(victim.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // The bystander's session is untouched by another account's compromise.
        var bystanderRefresh = await RefreshAsync(bystander.RefreshToken);
        bystanderRefresh.StatusCode.Should().Be(HttpStatusCode.OK);
        var bystanderRotated = await ReadAuthAsync(bystanderRefresh);
        (await LoadTokenAsync(bystanderRotated.RefreshToken)).RevokedAt.Should().BeNull();
        (await LoadAllTokensForUserAsync(bystander.User.Id)).Should().OnlyContain(t => t.UserId == bystander.User.Id);
    }

    [Fact]
    public async Task Logout_revokes_the_own_token()
    {
        var user = await _factory.RegisterAsync($"logout-own-{Guid.NewGuid():N}@test.com");

        var logout = await _factory.HttpClient.SendAsync(_factory.Authenticated(
            new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout")
            {
                Content = JsonContent.Create(new { refreshToken = user.RefreshToken })
            }, user.AccessToken));

        logout.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await LoadTokenAsync(user.RefreshToken)).RevokedAt.Should().NotBeNull();
        (await RefreshAsync(user.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Logout_with_another_users_token_does_not_revoke_it()
    {
        var victim = await _factory.RegisterAsync($"logout-victim-{Guid.NewGuid():N}@test.com");
        var attacker = await _factory.RegisterAsync($"logout-attacker-{Guid.NewGuid():N}@test.com");

        var logout = await _factory.HttpClient.SendAsync(_factory.Authenticated(
            new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout")
            {
                Content = JsonContent.Create(new { refreshToken = victim.RefreshToken })
            }, attacker.AccessToken));

        // Same response either way, so the endpoint cannot be used to probe for valid tokens.
        logout.StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await LoadTokenAsync(victim.RefreshToken)).RevokedAt.Should().BeNull("ownership is enforced");
        (await RefreshAsync(victim.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.OK,
            "the victim session must still work after an attacker's logout attempt");
    }

    [Fact]
    public async Task Logout_of_an_unknown_token_is_accepted_without_error()
    {
        var user = await _factory.RegisterAsync($"logout-unknown-{Guid.NewGuid():N}@test.com");

        var logout = await _factory.HttpClient.SendAsync(_factory.Authenticated(
            new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout")
            {
                Content = JsonContent.Create(new { refreshToken = "never-issued-token" })
            }, user.AccessToken));

        logout.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Logout_without_authentication_is_rejected()
    {
        var user = await _factory.RegisterAsync($"logout-anon-{Guid.NewGuid():N}@test.com");

        var logout = await _factory.HttpClient.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout")
        {
            Content = JsonContent.Create(new { refreshToken = user.RefreshToken })
        });

        logout.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await LoadTokenAsync(user.RefreshToken)).RevokedAt.Should().BeNull();
    }
}
