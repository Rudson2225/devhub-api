using System.Net;
using System.Net.Http.Json;
using FluentAssertions;

namespace DevHub.IntegrationTests;

/// <summary>
/// Covers H3: forwarded headers are enabled, but a client that is not a declared proxy cannot steer
/// the address the application sees or the address the rate limiter keys on.
/// </summary>
public sealed class UntrustedForwardedHeadersTests : IClassFixture<UntrustedProxyApiFactory>
{
    private readonly UntrustedProxyApiFactory _factory;

    public UntrustedForwardedHeadersTests(UntrustedProxyApiFactory factory) => _factory = factory;

    private Task<HttpResponseMessage> LoginFromAsync(string clientAddress, string email = "untrusted-probe@test.com")
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { email, password = "Wrong!Pass123" })
        };

        request.Headers.Add("X-Forwarded-For", clientAddress);
        return _factory.HttpClient.SendAsync(request);
    }

    [Fact]
    public async Task Forwarded_header_from_an_untrusted_sender_is_ignored()
    {
        // Every request carries a different forged address. If the header were honoured each one
        // would get a fresh budget; because the sender is not a known proxy they all collapse onto
        // the real peer address and the fourth request is rejected.
        var responses = new List<HttpResponseMessage>
        {
            await LoginFromAsync("198.51.100.1"),
            await LoginFromAsync("198.51.100.2"),
            await LoginFromAsync("198.51.100.3")
        };

        responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Unauthorized);

        var limited = await LoginFromAsync("198.51.100.4");
        limited.StatusCode.Should().Be(HttpStatusCode.TooManyRequests,
            "the forged addresses must not have created independent budgets");
    }

    // The trust decision is taken once per request before any X-Forwarded-* value is consumed, so
    // proving the address header is ignored also proves the proto header is ignored; both are
    // exercised together by the production-environment test that gates HSTS on the resolved scheme.
}
