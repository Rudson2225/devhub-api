using System.Net;
using System.Text.Json;
using FluentAssertions;

namespace DevHub.IntegrationTests;

/// <summary>
/// Covers M2: the generated contract has to tell a client which endpoints need a token and which
/// answers it can get back when it does not have one (401) or when it is not allowed (403).
/// </summary>
public sealed class OpenApiSecurityDocumentationTests : IClassFixture<DevHubApiFactory>
{
    private readonly DevHubApiFactory _factory;

    public OpenApiSecurityDocumentationTests(DevHubApiFactory factory) => _factory = factory;

    private async Task<JsonDocument> ReadDocumentAsync()
    {
        var response = await _factory.HttpClient.GetAsync("/swagger/v1/swagger.json");
        response.StatusCode.Should().Be(HttpStatusCode.OK, "the document must be generated in Development");
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");

        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    private static JsonElement Operation(JsonDocument document, string path, string method)
    {
        var operation = document.RootElement
            .GetProperty("paths")
            .GetProperty(path)
            .GetProperty(method);

        return operation;
    }

    [Fact]
    public async Task The_bearer_scheme_is_declared()
    {
        using var document = await ReadDocumentAsync();

        document.RootElement
            .GetProperty("components")
            .GetProperty("securitySchemes")
            .GetProperty("Bearer")
            .GetProperty("type").GetString().Should().Be("http");
    }

    [Fact]
    public async Task The_document_requires_a_token_by_default()
    {
        using var document = await ReadDocumentAsync();

        var security = document.RootElement.GetProperty("security");
        security.GetArrayLength().Should().Be(1, "operations that do not opt out inherit this");
        security[0].GetProperty("Bearer").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Protected_endpoints_document_401_403_and_keep_the_token_requirement()
    {
        using var document = await ReadDocumentAsync();

        foreach (var (path, method) in new[]
                 {
                     ("/api/projects", "get"),
                     ("/api/projects", "post"),
                     ("/api/users/me", "get"),
                     ("/api/auth/logout", "post")
                 })
        {
            var operation = Operation(document, path, method);
            var missing = new List<string>();

            // No opt-out means the document wide Bearer requirement still applies here.
            if (operation.TryGetProperty("security", out var security) && security.GetArrayLength() != 0)
            {
                missing.Add("security override");
            }

            if (!operation.GetProperty("responses").TryGetProperty("401", out var unauthorized)
                || string.IsNullOrWhiteSpace(unauthorized.GetProperty("description").GetString()))
            {
                missing.Add("401");
            }

            if (!operation.GetProperty("responses").TryGetProperty("403", out var forbidden)
                || string.IsNullOrWhiteSpace(forbidden.GetProperty("description").GetString()))
            {
                missing.Add("403");
            }

            missing.Should().BeEmpty($"{method} {path} must document {string.Join(", ", missing)}");
        }
    }

    [Fact]
    public async Task Public_endpoints_do_not_claim_to_need_a_token()
    {
        using var document = await ReadDocumentAsync();

        foreach (var (path, method) in new[]
                 {
                     ("/api/auth/login", "post"),
                     ("/api/auth/register", "post"),
                     ("/api/auth/refresh", "post")
                 })
        {
            var operation = Operation(document, path, method);

            // An empty array opts the operation out of the document wide requirement; no entry at all
            // would silently inherit it and tell clients a token is needed here.
            operation.TryGetProperty("security", out var security).Should().BeTrue(
                $"{method} {path} must opt out explicitly instead of inheriting the global requirement");
            security.GetArrayLength().Should().Be(0,
                $"{method} {path} is [AllowAnonymous] and must not advertise a credential requirement");
        }
    }
}
