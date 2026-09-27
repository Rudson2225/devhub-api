using System.Net;

namespace DevHub.Application.Common.Exceptions;

public class ApiException : Exception
{
    public HttpStatusCode StatusCode { get; }

    public string? ErrorCode { get; }

    public ApiException(HttpStatusCode statusCode, string message, string? errorCode = null)
        : base(message)
    {
        StatusCode = statusCode;
        ErrorCode = errorCode;
    }
}

public sealed class NotFoundException : ApiException
{
    public NotFoundException(string message)
        : base(HttpStatusCode.NotFound, message, "not_found")
    {
    }
}

public sealed class ConflictException : ApiException
{
    public ConflictException(string message)
        : base(HttpStatusCode.Conflict, message, "conflict")
    {
    }
}

public sealed class ForbiddenException : ApiException
{
    public ForbiddenException(string message)
        : base(HttpStatusCode.Forbidden, message, "forbidden")
    {
    }
}

public sealed class UnauthorizedException : ApiException
{
    public UnauthorizedException(string message)
        : base(System.Net.HttpStatusCode.Unauthorized, message, "unauthorized")
    {
    }
}

public sealed class BadRequestException : ApiException
{
    public BadRequestException(string message)
        : base(HttpStatusCode.BadRequest, message, "bad_request")
    {
    }
}

public sealed class ValidationException : ApiException
{
    public IReadOnlyDictionary<string, string[]> Errors { get; }

    public ValidationException(IReadOnlyDictionary<string, string[]> errors)
        : base(HttpStatusCode.UnprocessableEntity, "One or more validation errors occurred.", "validation_failed")
    {
        Errors = errors;
    }

    public ValidationException(string message, IReadOnlyDictionary<string, string[]> errors)
        : base(HttpStatusCode.UnprocessableEntity, message, "validation_failed")
    {
        Errors = errors;
    }
}
