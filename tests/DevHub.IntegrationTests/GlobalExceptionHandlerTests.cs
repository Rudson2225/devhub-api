using System.Net;
using System.Text;
using System.Text.Json;
using DevHub.Api.Middleware;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace DevHub.IntegrationTests;

/// <summary>
/// Covers the EF Core -> Npgsql unwrapping performed by <see cref="GlobalExceptionHandler"/> and
/// the guarantee that no SQL, schema, connection string or stack trace reaches the client.
/// </summary>
public sealed class GlobalExceptionHandlerTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static async Task<(HttpStatusCode Status, string Body, HttpContext Context)> HandleAsync(Exception exception)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = "/api/projects/abc/tags";
        context.Response.Body = new MemoryStream();

        var handler = new GlobalExceptionHandler(NullLogger<GlobalExceptionHandler>.Instance);
        var handled = await handler.TryHandleAsync(context, exception, CancellationToken.None);

        handled.Should().BeTrue();

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var body = await new StreamReader(context.Response.Body, Encoding.UTF8).ReadToEndAsync();
        return ((HttpStatusCode)context.Response.StatusCode, body, context);
    }

    private static PostgresException Postgres(string sqlState) =>
        new("duplicate key value violates unique constraint \"ix_tags_name\"", "ERROR", "ERROR", sqlState);

    private static DbUpdateException Wrapped(Exception exception, string? message = null) =>
        new(message ?? "An error occurred while saving the entity changes.", exception);

    [Fact]
    public async Task Unique_violation_wrapped_in_DbUpdateException_maps_to_409()
    {
        var (status, body, _) = await HandleAsync(Wrapped(Postgres(PostgresErrorCodes.UniqueViolation)));

        status.Should().Be(HttpStatusCode.Conflict);
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("status").GetInt32().Should().Be(409);
        doc.RootElement.GetProperty("errorCode").GetString().Should().Be("duplicate_key");
    }

    [Fact]
    public async Task Foreign_key_violation_wrapped_in_DbUpdateException_maps_to_409()
    {
        var (status, body, _) = await HandleAsync(Wrapped(Postgres(PostgresErrorCodes.ForeignKeyViolation)));

        status.Should().Be(HttpStatusCode.Conflict);
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("errorCode").GetString().Should().Be("foreign_key_violation");
    }

    [Fact]
    public async Task Not_null_violation_maps_to_400()
    {
        var (status, body, _) = await HandleAsync(Wrapped(Postgres(PostgresErrorCodes.NotNullViolation)));

        status.Should().Be(HttpStatusCode.BadRequest);
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("errorCode").GetString().Should().Be("invalid_value");
    }

    [Fact]
    public async Task Check_violation_maps_to_400()
    {
        var (status, _, _) = await HandleAsync(Wrapped(Postgres(PostgresErrorCodes.CheckViolation)));
        status.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData(PostgresErrorCodes.StringDataRightTruncation)]
    [InlineData(PostgresErrorCodes.NumericValueOutOfRange)]
    [InlineData(PostgresErrorCodes.InvalidTextRepresentation)]
    [InlineData(PostgresErrorCodes.InvalidDatetimeFormat)]
    public async Task Value_level_violations_map_to_400(string sqlState)
    {
        var (status, _, _) = await HandleAsync(Wrapped(Postgres(sqlState)));
        status.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData(PostgresErrorCodes.SerializationFailure)]
    [InlineData(PostgresErrorCodes.DeadlockDetected)]
    public async Task Retryable_concurrency_failures_map_to_409(string sqlState)
    {
        var (status, _, _) = await HandleAsync(Wrapped(Postgres(sqlState)));
        status.Should().Be(HttpStatusCode.Conflict);
    }

    [Theory]
    [InlineData(PostgresErrorCodes.TooManyConnections)]
    [InlineData(PostgresErrorCodes.CannotConnectNow)]
    [InlineData(PostgresErrorCodes.AdminShutdown)]
    [InlineData(PostgresErrorCodes.CrashShutdown)]
    public async Task Database_availability_failures_map_to_503(string sqlState)
    {
        var (status, _, _) = await HandleAsync(Wrapped(Postgres(sqlState)));
        status.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task Unknown_sql_state_is_not_reported_as_conflict()
    {
        var (status, body, _) = await HandleAsync(Wrapped(Postgres(PostgresErrorCodes.UndefinedTable)));

        status.Should().Be(HttpStatusCode.InternalServerError);
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("errorCode").GetString().Should().Be("internal_error");
    }

    [Fact]
    public async Task DbUpdateException_without_provider_exception_maps_to_500()
    {
        var (status, _, _) = await HandleAsync(Wrapped(new InvalidOperationException("no provider error")));
        status.Should().Be(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task DbUpdateConcurrencyException_maps_to_409()
    {
        var (status, _, _) = await HandleAsync(new DbUpdateConcurrencyException("row updated concurrently"));
        status.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Provider_detail_never_reaches_the_client()
    {
        var leaky = new PostgresException(
            "duplicate key value violates unique constraint \"ix_tags_name\"",
            "ERROR", "ERROR", PostgresErrorCodes.UniqueViolation,
            tableName: "tags", constraintName: "ix_tags_name");

        var (_, body, _) = await HandleAsync(
            Wrapped(leaky, "An error occurred while saving the entity changes. INSERT INTO \"public\".\"tags\" ..."));

        body.Should().NotContain("ix_tags_name");
        body.Should().NotContain("INSERT INTO");
        body.Should().NotContain("tags");
        body.Should().NotContain("duplicate key value violates");
        body.Should().NotContain("Npgsql");
        body.Should().NotContain("DevHub");
        body.Should().NotContain("Host=");
        body.Should().NotContain("at DevHub");
    }

    [Fact]
    public async Task Response_body_is_problem_details_with_error_code()
    {
        var (_, body, context) = await HandleAsync(Wrapped(Postgres(PostgresErrorCodes.UniqueViolation)));

        context.Response.ContentType.Should().StartWith("application/problem+json");
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.TryGetProperty("title", out _).Should().BeTrue();
        doc.RootElement.TryGetProperty("status", out _).Should().BeTrue();
        doc.RootElement.TryGetProperty("errorCode", out _).Should().BeTrue();
    }
}
