using System.Net;
using System.Net.Http.Json;
using FluentAssertions;

namespace DevHub.IntegrationTests;

/// <summary>
/// Covers H3: X-Forwarded-For is only trusted when the request arrives from a declared proxy, and the
/// resolved address is what drives the per-address rate limit budget.
/// </summary>
public sealed class ForwardedHeadersTests : IClassFixture<TrustedProxyApiFactory>
{
    private readonly TrustedProxyApiFactory _factory;

    public ForwardedHeadersTests(TrustedProxyApiFactory factory) => _factory = factory;

    private Task<HttpResponseMessage> LoginFromAsync(string clientAddress, string email = "proxy-probe@test.com")
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { email, password = "Wrong!Pass123" })
        };

        if (clientAddress is not null)
        {
            request.Headers.Add("X-Forwarded-For", clientAddress);
        }

        return _factory.HttpClient.SendAsync(request);
    }

    [Fact]
    public async Task Normal_requests_are_not_rate_limited()
    {
        var first = await LoginFromAsync("198.51.100.10");
        var second = await LoginFromAsync("198.51.100.10");
        var third = await LoginFromAsync("198.51.100.10");

        first.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        second.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        third.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "the budget is 3 per address");
    }

    [Fact]
    public async Task Trusted_proxy_resolves_the_real_client_address_for_the_rate_limit()
    {
        // Three requests from one client exhaust that client's budget.
        for (var i = 0; i < 3; i++)
        {
            (await LoginFromAsync("198.51.100.7")).StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests);
        }

        var limited = await LoginFromAsync("198.51.100.7");
        limited.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);

        // A different client behind the same proxy still has its own budget, which proves the limit
        // is keyed on the forwarded address and not on the proxy address.
        var other = await LoginFromAsync("198.51.100.99");
        other.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Forged_forward_chain_is_reduced_to_the_address_the_proxy_asserts()
    {
        // A client may prepend its own entries, but the trusted proxy only vouches for the last one
        // and ForwardLimit caps how many hops are consumed.
        var chained = await LoginFromAsync("1.2.3.4, 198.51.100.9");
        chained.StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests);

        // Exhausting the address the proxy actually asserted must be what triggers the limit.
        for (var i = 0; i < 2; i++)
        {
            (await LoginFromAsync("198.51.100.9")).StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests);
        }

        (await LoginFromAsync("198.51.100.9")).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);

        // The forged entry on the left was never consumed as the client address, so it is untouched.
        var forged = await LoginFromAsync("1.2.3.4");
        forged.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "the untrusted entry must not have been used as the rate limit key");
    }

    [Fact]
    public async Task Rate_limited_response_is_problem_details_with_retry_after()
    {
        for (var i = 0; i < 3; i++)
        {
            await LoginFromAsync("198.51.100.200");
        }

        var limited = await LoginFromAsync("198.51.100.200");

        limited.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        limited.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");

        var body = await limited.Content.ReadAsStringAsync();
        body.Should().Contain("\"errorCode\":\"rate_limit_exceeded\"");
        body.Should().Contain("\"status\":429");
        body.Should().NotContain("password");
        body.Should().NotContain("proxy-probe@test.com");

        limited.Headers.RetryAfter.Should().NotBeNull("Retry-After tells the caller when to come back");
        limited.Headers.RetryAfter!.Delta.Should().BePositive();
        limited.Headers.RetryAfter.Delta.Should().BeLessThanOrEqualTo(TimeSpan.FromMinutes(2));
    }
}
