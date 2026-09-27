using System.Net;
using System.Net.Http.Json;
using FluentAssertions;

namespace DevHub.IntegrationTests;

/// <summary>
/// Covers the per-account part of H3: the address budget is not the only one, so rotating the source
/// address cannot lift the limit on one account. Also covers M3 for the account-limited rejection.
/// </summary>
public sealed class AccountRateLimitTests : IClassFixture<AccountRateLimitApiFactory>
{
    private const string Account = "account-probe@test.com";

    private readonly AccountRateLimitApiFactory _factory;

    public AccountRateLimitTests(AccountRateLimitApiFactory factory) => _factory = factory;

    private Task<HttpResponseMessage> LoginFromAsync(string clientAddress, string email)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { email, password = "Wrong!Pass123" })
        };

        request.Headers.Add("X-Forwarded-For", clientAddress);
        return _factory.HttpClient.SendAsync(request);
    }

    [Fact]
    public async Task Rotating_source_addresses_does_not_lift_the_limit_on_one_account()
    {
        var addresses = new[] { "203.0.113.1", "203.0.113.2", "203.0.113.3", "203.0.113.4", "203.0.113.5" };

        var statuses = new List<HttpStatusCode>();
        foreach (var address in addresses)
        {
            statuses.Add((await LoginFromAsync(address, Account)).StatusCode);
        }

        statuses.Take(3).Should().OnlyContain(s => s == HttpStatusCode.Unauthorized,
            "each source address has its own budget, so the address budget must not reject them");
        statuses.Skip(3).Should().OnlyContain(s => s == HttpStatusCode.TooManyRequests,
            "the shared account budget must reject once the address budget is out of the way");
    }

    [Fact]
    public async Task Other_accounts_keep_their_own_budget()
    {
        // Exhaust a fresh account through several addresses.
        for (var i = 0; i < 4; i++)
        {
            await LoginFromAsync($"198.51.100.{i}", "other-account@test.com");
        }

        var other = await LoginFromAsync("198.51.100.50", "another-account@test.com");
        other.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "one account hitting its limit must not affect another");
    }

    [Fact]
    public async Task Account_rejection_is_problem_details_with_retry_after()
    {
        for (var i = 0; i < 4; i++)
        {
            await LoginFromAsync($"198.51.101.{i}", "shape-probe@test.com");
        }

        var limited = await LoginFromAsync("198.51.101.4", "shape-probe@test.com");

        limited.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        limited.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");

        var body = await limited.Content.ReadAsStringAsync();
        body.Should().Contain("\"errorCode\":\"rate_limit_exceeded\"");
        body.Should().Contain("\"status\":429");
        body.Should().NotContain("Wrong!Pass123");
        body.Should().NotContain("shape-probe@test.com");

        limited.Headers.RetryAfter.Should().NotBeNull();
        limited.Headers.RetryAfter!.Delta.Should().BePositive();
    }
}
