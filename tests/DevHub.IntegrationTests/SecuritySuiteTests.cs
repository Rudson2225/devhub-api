using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;

namespace DevHub.IntegrationTests;

/// <summary>
/// The security cases every endpoint of an API like this has to answer for: no account discovery
/// through the login form, no acceptance of forged or unsigned tokens, no way to mass-assign fields
/// the caller has no business choosing, and no reach into somebody else's project.
/// </summary>
public sealed class SecuritySuiteTests : IClassFixture<DevHubApiFactory>
{
    private readonly DevHubApiFactory _factory;

    public SecuritySuiteTests(DevHubApiFactory factory) => _factory = factory;

    private static string Base64Url(string value)
        => Convert.ToBase64String(Encoding.UTF8.GetBytes(value))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    [Fact]
    public async Task Login_cannot_be_used_to_discover_which_accounts_exist()
    {
        var known = await _factory.RegisterAsync($"enum-{Guid.NewGuid():N}@test.com");

        var unknownAccount = await _factory.HttpClient.PostAsJsonAsync("/api/auth/login",
            new { email = "no-such-account@test.com", password = "Wrong!Pass123" });
        var wrongPassword = await _factory.HttpClient.PostAsJsonAsync("/api/auth/login",
            new { email = known.User.Email, password = "Wrong!Pass123" });

        unknownAccount.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        wrongPassword.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        unknownAccount.Content.Headers.ContentType!.MediaType
            .Should().Be(wrongPassword.Content.Headers.ContentType!.MediaType);

        (await ProblemBody.WithoutTraceIdAsync(unknownAccount))
            .Should().Be(await ProblemBody.WithoutTraceIdAsync(wrongPassword),
                "an unknown address and a wrong password must be indistinguishable");
    }

    [Fact]
    public async Task A_tampered_access_token_gets_the_same_answer_as_no_token_at_all()
    {
        var auth = await _factory.RegisterAsync($"tamper-{Guid.NewGuid():N}@test.com");

        var missing = await _factory.HttpClient.GetAsync("/api/users/me");

        var parts = auth.AccessToken.Split('.');
        var signature = parts[2];
        var flipped = (signature[0] == 'A' ? 'B' : 'A') + signature[1..];
        var tampered = new HttpRequestMessage(HttpMethod.Get, "/api/users/me");
        tampered.Headers.Authorization = new("Bearer", $"{parts[0]}.{parts[1]}.{flipped}");
        var rejected = await _factory.HttpClient.SendAsync(tampered);

        rejected.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        rejected.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        (await ProblemBody.WithoutTraceIdAsync(rejected))
            .Should().Be(await ProblemBody.WithoutTraceIdAsync(missing),
                "a forged signature must not be reported differently from an absent one");
    }

    [Fact]
    public async Task An_unsigned_none_algorithm_token_is_refused()
    {
        var auth = await _factory.RegisterAsync($"alg-{Guid.NewGuid():N}@test.com");
        var realParts = auth.AccessToken.Split('.');

        var unsigned = $"{Base64Url("{\"alg\":\"none\",\"typ\":\"JWT\"}")}.{realParts[1]}.";
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/users/me");
        request.Headers.Authorization = new("Bearer", unsigned);

        var response = await _factory.HttpClient.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "the handler must never fall back to accepting a token without a signature");
        (await ProblemBody.WithoutTraceIdAsync(response))
            .Should().NotContain("alg", "the reason must not be echoed back to the caller");
    }

    [Fact]
    public async Task Extra_fields_in_the_body_cannot_be_used_to_choose_the_owner()
    {
        var caller = await _factory.RegisterAsync($"mass-{Guid.NewGuid():N}@test.com");

        var response = await _factory.HttpClient.SendAsync(
            _factory.Authenticated(
                new HttpRequestMessage(HttpMethod.Post, "/api/projects")
                {
                    Content = JsonContent.Create(new
                    {
                        name = "Mass assignment probe",
                        id = Guid.NewGuid(),
                        ownerId = Guid.NewGuid(),
                        ownerName = "someone-else",
                        memberCount = 99,
                        memberIds = new[] { Guid.NewGuid() }
                    })
                },
                caller.AccessToken));

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("ownerId").GetGuid().Should().Be(caller.User.Id,
            "only the authenticated caller may be recorded as the owner");
        body.RootElement.GetProperty("memberCount").GetInt32().Should().Be(1);
        body.RootElement.GetProperty("id").GetGuid().Should().NotBe(Guid.Empty);
        body.RootElement.TryGetProperty("ownerName", out _).Should().BeTrue();
        body.RootElement.GetProperty("ownerName").GetString().Should().Be(caller.User.DisplayName);
    }

    [Fact]
    public async Task Another_account_cannot_reach_the_tasks_of_a_project_it_is_not_in()
    {
        var owner = await _factory.RegisterAsync($"tenant-{Guid.NewGuid():N}@test.com");
        var outsider = await _factory.RegisterAsync($"outsider-{Guid.NewGuid():N}@test.com");

        var project = await _factory.CreateProjectAsync(owner.AccessToken, "Private tenant");
        var created = await _factory.HttpClient.SendAsync(
            _factory.Authenticated(
                new HttpRequestMessage(HttpMethod.Post, $"/api/projects/{project.Id}/tasks")
                {
                    Content = JsonContent.Create(new { title = "Confidential work" })
                },
                owner.AccessToken));
        created.StatusCode.Should().Be(HttpStatusCode.Created);

        foreach (var path in new[]
                 {
                     $"/api/projects/{project.Id}/tasks",
                     $"/api/projects/{project.Id}/members",
                     $"/api/projects/{project.Id}/activities"
                 })
        {
            var response = await _factory.HttpClient.SendAsync(
                _factory.Authenticated(new HttpRequestMessage(HttpMethod.Get, path), outsider.AccessToken));

            response.StatusCode.Should().Be(HttpStatusCode.Forbidden, $"{path} must stay closed");
            (await ProblemBody.WithoutTraceIdAsync(response)).Should().NotContain("Confidential work",
                "the refusal must not echo the other project's data");
        }
    }
}
