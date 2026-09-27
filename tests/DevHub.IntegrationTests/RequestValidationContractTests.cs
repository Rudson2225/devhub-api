using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;

namespace DevHub.IntegrationTests;

/// <summary>
/// Covers M11: the two families of input rejection have to be told apart consistently.
/// 400 means the request could not be understood at all (malformed JSON, no body, wrong type) and
/// 422 means it was understood but the business rules said no. Both must answer with the very same
/// problem+json contract, media type included.
/// </summary>
public sealed class RequestValidationContractTests : IClassFixture<DevHubApiFactory>
{
    private readonly DevHubApiFactory _factory;
    private string? _token;

    public RequestValidationContractTests(DevHubApiFactory factory) => _factory = factory;

    private async Task<string> TokenAsync()
    {
        // One account per test: xUnit builds a fresh class instance for every case, so a shared
        // address would collide on registration for the second case.
        if (_token is null)
        {
            var auth = await _factory.RegisterAsync($"validation-{Guid.NewGuid():N}@test.com");
            _token = auth.AccessToken;
        }

        return _token;
    }

    private async Task<HttpResponseMessage> PostProjectAsync(HttpContent content)
    {
        var request = _factory.Authenticated(
            new HttpRequestMessage(HttpMethod.Post, "/api/projects") { Content = content },
            await TokenAsync());

        return await _factory.HttpClient.SendAsync(request);
    }

    private static async Task<JsonElement> ProblemAsync(HttpResponseMessage response)
    {
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json",
            "every rejection uses the RFC 9457 media type");
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    private static void ShouldBeTheStandardProblem(JsonElement problem, int expectedStatus, string expectedErrorCode)
    {
        problem.GetProperty("status").GetInt32().Should().Be(expectedStatus);
        problem.GetProperty("title").GetString().Should().NotBeNullOrWhiteSpace();
        problem.GetProperty("errorCode").GetString().Should().Be(expectedErrorCode);
        problem.GetProperty("traceId").GetString().Should().NotBeNullOrWhiteSpace(
            "an error without a correlation id cannot be traced back to the log");
        problem.GetProperty("errors").ValueKind.Should().Be(JsonValueKind.Object);
        problem.TryGetProperty("stackTrace", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Malformed_json_is_a_400_with_an_error_code()
    {
        var response = await PostProjectAsync(
            new StringContent("{ not json", Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        ShouldBeTheStandardProblem(await ProblemAsync(response), 400, "bad_request");
    }

    [Fact]
    public async Task Empty_body_is_a_400_with_an_error_code()
    {
        var response = await PostProjectAsync(
            new StringContent(string.Empty, Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        ShouldBeTheStandardProblem(await ProblemAsync(response), 400, "bad_request");
    }

    [Fact]
    public async Task A_value_of_the_wrong_type_is_a_400_and_not_a_422()
    {
        var response = await PostProjectAsync(JsonContent.Create(new { name = 123 }));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "a request that cannot be bound is not yet a business rule violation");
        ShouldBeTheStandardProblem(await ProblemAsync(response), 400, "bad_request");
    }

    [Fact]
    public async Task A_business_rule_violation_is_a_422()
    {
        var response = await PostProjectAsync(JsonContent.Create(new { name = "" }));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var problem = await ProblemAsync(response);
        ShouldBeTheStandardProblem(problem, 422, "validation_failed");
        problem.GetProperty("errors").GetProperty("Name").GetArrayLength().Should().BeGreaterThan(0,
            "the field level messages are what the client shows to the user");
    }

    [Fact]
    public async Task The_two_answers_are_distinguishable_by_status_and_code_only()
    {
        var unbindable = await ProblemAsync(await PostProjectAsync(
            new StringContent("[", Encoding.UTF8, "application/json")));
        var invalid = await ProblemAsync(await PostProjectAsync(JsonContent.Create(new { name = "" })));

        unbindable.GetProperty("status").GetInt32().Should().Be(400);
        invalid.GetProperty("status").GetInt32().Should().Be(422);
        unbindable.GetProperty("errorCode").GetString().Should().Be("bad_request");
        invalid.GetProperty("errorCode").GetString().Should().Be("validation_failed");

        unbindable.GetProperty("title").GetString().Should().NotBe(invalid.GetProperty("title").GetString(),
            "the two situations must not read the same");
    }
}
