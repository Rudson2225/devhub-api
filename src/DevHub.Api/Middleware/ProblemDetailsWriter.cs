using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace DevHub.Api.Middleware;

/// <summary>
/// Single place where error payloads are serialised, so every failure path in the pipeline emits the
/// same ProblemDetails shape with the same media type. Used by the exception handler, the JWT bearer
/// challenge/forbid events and the rate limiter rejection callback, which all run outside of MVC.
/// </summary>
internal static class ProblemDetailsWriter
{
    private const string ContentType = "application/problem+json";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static Task WriteAsync(
        HttpContext context,
        int statusCode,
        string title,
        string errorCode,
        IDictionary<string, object?>? extensions = null,
        CancellationToken cancellationToken = default)
    {
        var problemDetails = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Extensions =
            {
                ["errorCode"] = errorCode,
                // Echoed back so a support request can be tied to the request log line without the
                // client having to capture anything else.
                ["traceId"] = context.TraceIdentifier
            }
        };

        if (extensions is not null)
        {
            foreach (var pair in extensions)
            {
                problemDetails.Extensions[pair.Key] = pair.Value;
            }
        }

        context.Response.StatusCode = statusCode;
        return context.Response.WriteAsJsonAsync(problemDetails, JsonOptions, ContentType, cancellationToken);
    }

    public static Task WriteRateLimitedAsync(HttpContext context, TimeSpan retryAfter, CancellationToken cancellationToken = default)
    {
        // A fixed window only ever needs the caller to wait out the remainder of the window.
        var seconds = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));
        context.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);

        return WriteAsync(
            context,
            StatusCodes.Status429TooManyRequests,
            "Too many requests",
            "rate_limit_exceeded",
            extensions: new Dictionary<string, object?> { ["retryAfter"] = seconds },
            cancellationToken: cancellationToken);
    }
}

/// <summary>
/// Answer used by the automatic model state filter. It has to be a result of its own because the
/// controllers declare <c>[Produces("application/json")]</c>, which would otherwise stamp every
/// rejected request with the wrong media type and drop the error code the rest of the API always
/// sends alongside the field level messages.
/// </summary>
internal sealed class ProblemResult : ActionResult
{
    private readonly int _statusCode;
    private readonly string _title;
    private readonly string _errorCode;
    private readonly IDictionary<string, object?>? _extensions;

    public ProblemResult(
        int statusCode,
        string title,
        string errorCode,
        IDictionary<string, object?>? extensions = null)
    {
        _statusCode = statusCode;
        _title = title;
        _errorCode = errorCode;
        _extensions = extensions;
    }

    public override Task ExecuteResultAsync(ActionContext context)
        => ProblemDetailsWriter.WriteAsync(
            context.HttpContext,
            _statusCode,
            _title,
            _errorCode,
            _extensions,
            context.HttpContext.RequestAborted);
}
