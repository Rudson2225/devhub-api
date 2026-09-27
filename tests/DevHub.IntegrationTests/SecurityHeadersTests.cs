using System.Net;
using FluentAssertions;

namespace DevHub.IntegrationTests;

/// <summary>
/// Covers M4: the response headers that constrain a browser, including the fact that Swagger UI is
/// the only route allowed to load its own assets.
/// </summary>
public sealed class SecurityHeadersTests : IClassFixture<DevHubApiFactory>
{
    private const string StrictCsp =
        "default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'";

    private readonly DevHubApiFactory _factory;

    public SecurityHeadersTests(DevHubApiFactory factory) => _factory = factory;

    private static string Header(HttpResponseMessage response, string name)
        => response.Headers.TryGetValues(name, out var values)
            ? string.Join(";", values)
            : "<missing>";

    [Fact]
    public async Task Json_responses_carry_the_strict_security_headers()
    {
        var response = await _factory.HttpClient.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");

        Header(response, "X-Content-Type-Options").Should().Be("nosniff");
        Header(response, "X-Frame-Options").Should().Be("DENY");
        Header(response, "Referrer-Policy").Should().Be("no-referrer");
        Header(response, "Content-Security-Policy").Should().Be(StrictCsp,
            "an API response must not be allowed to load or frame anything");
    }

    [Fact]
    public async Task Protected_endpoints_get_the_same_headers_as_public_ones()
    {
        var response = await _factory.HttpClient.GetAsync("/api/users/me");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        Header(response, "X-Content-Type-Options").Should().Be("nosniff");
        Header(response, "X-Frame-Options").Should().Be("DENY");
        Header(response, "Referrer-Policy").Should().Be("no-referrer");
        Header(response, "Content-Security-Policy").Should().Be(StrictCsp);
    }

    [Fact]
    public async Task Swagger_ui_keeps_a_policy_that_does_not_break_it()
    {
        var response = await _factory.HttpClient.GetAsync("/swagger/index.html");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/html");

        var policy = Header(response, "Content-Security-Policy");
        policy.Should().NotBe(StrictCsp, "the strict policy would block every Swagger UI asset");
        policy.Should().Contain("default-src 'self'");
        policy.Should().Contain("script-src 'self'");
        policy.Should().Contain("frame-ancestors 'none'", "the UI must still not be framed");

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("swagger-ui");
    }

    [Fact]
    public async Task The_machine_readable_document_keeps_the_strict_policy()
    {
        var response = await _factory.HttpClient.GetAsync("/swagger/v1/swagger.json");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        Header(response, "Content-Security-Policy").Should().Be(StrictCsp,
            "a JSON document gains nothing from the looser policy the UI needs");
    }

    [Fact]
    public async Task Hsts_is_not_sent_over_plain_http_in_development()
    {
        var response = await _factory.HttpClient.GetAsync("/health");

        response.Headers.Contains("Strict-Transport-Security").Should().BeFalse(
            "pinning HTTPS on a plain HTTP development server would be harmful");
    }
}
