using System.Net;
using System.Text.Json;
using FluentAssertions;

namespace DevHub.IntegrationTests;

/// <summary>
/// Covers M2: the 401 and 403 answers the document promises have to be real, carry the
/// application/problem+json contract, and must not leak any hint about why a token was refused.
/// </summary>
public sealed class AuthenticationResponseContractTests : IClassFixture<DevHubApiFactory>
{
    private readonly DevHubApiFactory _factory;

    public AuthenticationResponseContractTests(DevHubApiFactory factory) => _factory = factory;

    private static async Task<(HttpStatusCode Status, string Media, string Body, string? Challenge)> SendAsync(
        DevHubApiFactory factory,
        string path,
        string? token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (token is not null)
        {
            request.Headers.Authorization = new("Bearer", token);
        }

        var response = await factory.HttpClient.SendAsync(request);
        var challenge = response.Headers.TryGetValues("WWW-Authenticate", out var values)
            ? string.Join(", ", values)
            : null;

        return (response.StatusCode,
            response.Content.Headers.ContentType?.MediaType ?? "",
            await response.Content.ReadAsStringAsync(),
            challenge);
    }

    [Fact]
    public async Task Missing_token_gets_a_401_problem_details_with_a_challenge()
    {
        var (status, media, body, challenge) = await SendAsync(_factory, "/api/users/me", null);

        status.Should().Be(HttpStatusCode.Unauthorized);
        media.Should().Be("application/problem+json");
        challenge.Should().Be("Bearer", "the client needs to know how to authenticate");

        using var problem = JsonDocument.Parse(body);
        problem.RootElement.GetProperty("status").GetInt32().Should().Be(401);
        problem.RootElement.GetProperty("title").GetString().Should().Be("Authentication required");
        problem.RootElement.GetProperty("errorCode").GetString().Should().Be("unauthorized");
        problem.RootElement.TryGetProperty("stackTrace", out _).Should().BeFalse();
        problem.RootElement.TryGetProperty("detail", out _).Should().BeFalse(
            "the reason must not hint at anything about the account");
    }

    [Fact]
    public async Task Invalid_token_gets_exactly_the_same_answer_as_a_missing_one()
    {
        var missing = await SendAsync(_factory, "/api/users/me", null);
        var invalid = await SendAsync(_factory, "/api/users/me", "not-a-jwt-at-all");

        invalid.Status.Should().Be(missing.Status, "a bad token must not be distinguishable");
        invalid.Media.Should().Be(missing.Media);
        invalid.Challenge.Should().Be(missing.Challenge);
        (await ProblemBody.WithoutTraceIdAsync(invalid.Body))
            .Should().Be(await ProblemBody.WithoutTraceIdAsync(missing.Body),
                "no oracle for token or account existence");
    }

    [Fact]
    public async Task Authenticated_but_not_permitted_gets_a_403_problem_details()
    {
        var owner = await _factory.RegisterAsync("contract-owner@test.com");
        var outsider = await _factory.RegisterAsync("contract-outsider@test.com");
        var project = await _factory.CreateProjectAsync(owner.AccessToken, "Contract project");

        var request = _factory.Authenticated(
            new HttpRequestMessage(HttpMethod.Get, $"/api/projects/{project.Id}"),
            outsider.AccessToken);
        var response = await _factory.HttpClient.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");

        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        problem.RootElement.GetProperty("status").GetInt32().Should().Be(403);
        problem.RootElement.GetProperty("errorCode").GetString().Should().Be("forbidden");
        problem.RootElement.TryGetProperty("stackTrace", out _).Should().BeFalse();
        problem.RootElement.GetProperty("title").GetString().Should().NotContain("token");
    }
}
