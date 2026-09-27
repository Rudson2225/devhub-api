namespace DevHub.Application.DTOs.Auth;

public sealed record RegisterRequest(string Email, string Password, string DisplayName);

public sealed record LoginRequest(string Email, string Password);

public sealed record RefreshTokenRequest(string RefreshToken);

public sealed record LogoutRequest(string RefreshToken);

public sealed record AuthResponse(
    string AccessToken,
    string RefreshToken,
    int ExpiresIn,
    string TokenType,
    UserInfo User);

public sealed record UserInfo(Guid Id, string Email, string DisplayName);

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);
