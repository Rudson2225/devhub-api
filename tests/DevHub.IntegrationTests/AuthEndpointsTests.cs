using System.Net;
using System.Net.Http.Json;
using DevHub.Application.DTOs.Auth;
using DevHub.Application.DTOs.Users;
using FluentAssertions;

namespace DevHub.IntegrationTests;

public sealed class AuthEndpointsTests : IClassFixture<DevHubApiFactory>
{
    private readonly DevHubApiFactory _factory;

    public AuthEndpointsTests(DevHubApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Register_login_and_me_flow_succeeds()
    {
        var auth = await _factory.RegisterAsync("auth-flow@test.com");

        auth.AccessToken.Should().NotBeNullOrEmpty();
        auth.RefreshToken.Should().NotBeNullOrEmpty();
        auth.TokenType.Should().Be("Bearer");
        auth.User.Email.Should().Be("auth-flow@test.com");

        var meRequest = _factory.Authenticated(new HttpRequestMessage(HttpMethod.Get, "/api/users/me"), auth.AccessToken);
        var me = await _factory.HttpClient.SendAsync(meRequest);

        me.StatusCode.Should().Be(HttpStatusCode.OK);
        var user = await me.Content.ReadFromJsonAsync<UserResponse>();
        user!.Email.Should().Be("auth-flow@test.com");
        user.DisplayName.Should().Be("auth-flow");
    }

    [Fact]
    public async Task Refresh_rotates_token_and_rejects_reuse()
    {
        var auth = await _factory.RegisterAsync("refresh-flow@test.com");

        var first = await _factory.HttpClient.PostAsJsonAsync("/api/auth/refresh", new RefreshTokenRequest(auth.RefreshToken));
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        var rotated = (await first.Content.ReadFromJsonAsync<AuthResponse>())!;
        rotated.RefreshToken.Should().NotBe(auth.RefreshToken);

        var reuse = await _factory.HttpClient.PostAsJsonAsync("/api/auth/refresh", new RefreshTokenRequest(auth.RefreshToken));
        reuse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_with_wrong_password_returns_401()
    {
        await _factory.RegisterAsync("wrong-pass@test.com");

        var response = await _factory.HttpClient.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest("wrong-pass@test.com", "Wrong!Pass123"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Register_with_weak_password_returns_422()
    {
        var response = await _factory.HttpClient.PostAsJsonAsync("/api/auth/register", new
        {
            email = "weak@test.com",
            password = "short",
            displayName = "Weak"
        });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Register_duplicate_email_returns_409()
    {
        await _factory.RegisterAsync("duplicate@test.com");

        var response = await _factory.HttpClient.PostAsJsonAsync("/api/auth/register", new
        {
            email = "DUPLICATE@test.com",
            password = "Str0ng!Pass123",
            displayName = "Dup"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Logout_revokes_refresh_token()
    {
        var auth = await _factory.RegisterAsync("logout-flow@test.com");

        var logout = await _factory.HttpClient.SendAsync(
            _factory.Authenticated(
                new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout")
                {
                    Content = JsonContent.Create(new LogoutRequest(auth.RefreshToken))
                },
                auth.AccessToken));
        logout.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var refresh = await _factory.HttpClient.PostAsJsonAsync("api/auth/refresh", new RefreshTokenRequest(auth.RefreshToken));
        refresh.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Unauthenticated_requests_are_rejected()
    {
        var response = await _factory.HttpClient.GetAsync("/api/users/me");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Auth_endpoints_are_rate_limited()
    {
        const string rateLimitKey = "RateLimit__AuthPolicy__PermitLimit";
        Environment.SetEnvironmentVariable(rateLimitKey, "3");
        try
        {
            using var factory = new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program>();
            using var client = factory.CreateClient();

            var statuses = new List<HttpStatusCode>();
            for (var i = 0; i < 6; i++)
            {
                var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("rate@test.com", "Wrong!Pass123"));
                statuses.Add(response.StatusCode);
            }

            statuses.Take(3).Should().AllBeEquivalentTo(HttpStatusCode.Unauthorized);
            statuses.Skip(3).Should().AllBeEquivalentTo(HttpStatusCode.TooManyRequests);
        }
        finally
        {
            Environment.SetEnvironmentVariable(rateLimitKey, "1000");
        }
    }

    [Fact]
    public async Task Logout_is_rate_limited_like_the_other_auth_routes()
    {
        const string rateLimitKey = "RateLimit__AuthPolicy__PermitLimit";
        Environment.SetEnvironmentVariable(rateLimitKey, "3");
        try
        {
            using var factory = new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program>();
            using var client = factory.CreateClient();

            // The limiter runs ahead of authentication, so even a request with no credentials at all
            // has to be accounted for; otherwise logout would be the one route left unguarded.
            var statuses = new List<HttpStatusCode>();
            for (var i = 0; i < 6; i++)
            {
                var response = await client.PostAsJsonAsync("/api/auth/logout", new { refreshToken = "" });
                statuses.Add(response.StatusCode);
            }

            statuses.Take(3).Should().AllBeEquivalentTo(HttpStatusCode.Unauthorized);
            statuses.Skip(3).Should().AllBeEquivalentTo(HttpStatusCode.TooManyRequests);
        }
        finally
        {
            Environment.SetEnvironmentVariable(rateLimitKey, "1000");
        }
    }
}
