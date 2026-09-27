namespace DevHub.Application.DTOs.Users;

public sealed record UserResponse(
    Guid Id,
    string Email,
    string DisplayName,
    string? Bio,
    string? AvatarUrl,
    DateTime CreatedAt);

public sealed record UpdateProfileRequest(string DisplayName, string? Bio, string? AvatarUrl);
