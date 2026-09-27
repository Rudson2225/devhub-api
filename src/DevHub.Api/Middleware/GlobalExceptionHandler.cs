using System.Net;
using System.Text.Json;
using DevHub.Application.Common.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DevHub.Api.Middleware;

public sealed class GlobalExceptionHandler : IExceptionHandler
{
    private const string GenericServerError = "An unexpected error occurred. Please try again later.";
    private const string ProblemContentType = "application/problem+json";

    private static readonly JsonSerializerOptions ProblemJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger)
    {
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (status, title, errorCode, errors) = MapException(exception);

        if (status >= HttpStatusCode.InternalServerError)
        {
            _logger.LogError(exception, "Unhandled exception during {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);
        }
        else
        {
            _logger.LogWarning("Handled client error {ErrorCode} during {Method} {Path}: {Message}", errorCode, httpContext.Request.Method, httpContext.Request.Path, exception.Message);
        }

        var problemDetails = new ProblemDetails
        {
            Status = (int)status,
            Title = title,
            Extensions =
            {
                ["errorCode"] = errorCode,
                ["traceId"] = httpContext.TraceIdentifier
            }
        };

        if (errors is not null)
        {
            problemDetails.Extensions["errors"] = errors;
        }

        httpContext.Response.StatusCode = (int)status;
        await httpContext.Response.WriteAsJsonAsync(problemDetails, ProblemJsonOptions, ProblemContentType, cancellationToken);

        return true;
    }

    private static (HttpStatusCode Status, string Title, string? ErrorCode, IReadOnlyDictionary<string, string[]>? Errors) MapException(Exception exception)
    {
        if (exception is ApiException api)
        {
            return (api.StatusCode, api.Message, api.ErrorCode, (api as ValidationException)?.Errors);
        }

        if (exception is DbUpdateConcurrencyException)
        {
            return (HttpStatusCode.Conflict, "The record was modified by another request. Reload and try again.", "concurrency_conflict", null);
        }

        if (TryGetPostgresException(exception, out var postgresException))
        {
            return MapPostgresException(postgresException);
        }

        return exception switch
        {
            BadHttpRequestException =>
                (HttpStatusCode.BadRequest, "The request is malformed.", "bad_request", null),
            UnauthorizedAccessException =>
                (HttpStatusCode.Forbidden, "You do not have permission to perform this action.", "forbidden", null),
            _ => (HttpStatusCode.InternalServerError, GenericServerError, "internal_error", null)
        };
    }

    /// <summary>
    /// EF Core wraps the provider exception, so a <see cref="PostgresException"/> is only reachable
    /// by walking the <see cref="Exception.InnerException"/> chain. Provider messages, SQL text and
    /// constraint details are never forwarded to the client.
    /// </summary>
    private static bool TryGetPostgresException(Exception exception, out PostgresException postgresException)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException candidate)
            {
                postgresException = candidate;
                return true;
            }
        }

        postgresException = null!;
        return false;
    }

    private static (HttpStatusCode Status, string Title, string? ErrorCode, IReadOnlyDictionary<string, string[]>? Errors) MapPostgresException(PostgresException exception)
        => exception.SqlState switch
        {
            // The submitted payload conflicts with a value that already exists.
            PostgresErrorCodes.UniqueViolation =>
                (HttpStatusCode.Conflict, "A resource with the same unique value already exists.", "duplicate_key", null),

            // A referenced row is missing or still referenced; either way the write cannot be applied.
            PostgresErrorCodes.ForeignKeyViolation =>
                (HttpStatusCode.Conflict, "The operation references a resource that does not exist.", "foreign_key_violation", null),

            // The payload was accepted by the transport layer but violates a column or check constraint.
            PostgresErrorCodes.NotNullViolation or
            PostgresErrorCodes.CheckViolation or
            PostgresErrorCodes.StringDataRightTruncation or
            PostgresErrorCodes.NumericValueOutOfRange or
            PostgresErrorCodes.InvalidTextRepresentation or
            PostgresErrorCodes.InvalidDatetimeFormat =>
                (HttpStatusCode.BadRequest, "One or more values in the request are invalid.", "invalid_value", null),

            // Transaction lost a race with a concurrent writer; the caller may safely retry.
            PostgresErrorCodes.SerializationFailure or
            PostgresErrorCodes.DeadlockDetected =>
                (HttpStatusCode.Conflict, "The request conflicted with a concurrent operation. Try again.", "concurrent_operation", null),

            // Database is momentarily unable to serve the request.
            PostgresErrorCodes.TooManyConnections or
            PostgresErrorCodes.CannotConnectNow or
            PostgresErrorCodes.AdminShutdown or
            PostgresErrorCodes.CrashShutdown =>
                (HttpStatusCode.ServiceUnavailable, "The service is temporarily unavailable. Try again later.", "service_unavailable", null),

            // Anything else is a server-side defect: reported as 500 without leaking provider detail.
            _ => (HttpStatusCode.InternalServerError, GenericServerError, "internal_error", null)
        };
}
