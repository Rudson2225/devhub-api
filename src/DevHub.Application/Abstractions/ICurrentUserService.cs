using System.Security.Claims;

namespace DevHub.Application.Abstractions;

public interface ICurrentUserService
{
    Guid? UserId { get; }

    string? Email { get; }

    string? DisplayName { get; }

    bool IsAuthenticated { get; }

    ClaimsPrincipal? Principal { get; }
}
