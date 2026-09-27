using DevHub.Application.Abstractions;
using DevHub.Application.Common.Exceptions;
using DevHub.Application.DTOs.Auth;
using DevHub.Application.Options;
using DevHub.Domain.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DevHub.Application.UseCases;

public interface IAuthService
{
    Task<AuthResponse> RegisterAsync(RegisterRequest request, string? ipAddress, CancellationToken cancellationToken = default);

    Task<AuthResponse> LoginAsync(LoginRequest request, string? ipAddress, CancellationToken cancellationToken = default);

    Task<AuthResponse> RefreshAsync(RefreshTokenRequest request, string? ipAddress, CancellationToken cancellationToken = default);

    Task LogoutAsync(Guid userId, LogoutRequest request, CancellationToken cancellationToken = default);

    Task ChangePasswordAsync(Guid userId, ChangePasswordRequest request, CancellationToken cancellationToken = default);
}

public sealed class AuthService : IAuthService
{
    private readonly IUserRepository _users;
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly IPasswordService _passwords;
    private readonly IAccessTokenGenerator _accessTokenGenerator;
    private readonly IRefreshTokenGenerator _refreshTokenGenerator;
    private readonly IUnitOfWork _unitOfWork;
    private readonly JwtOptions _jwtOptions;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        IUserRepository users,
        IRefreshTokenRepository refreshTokens,
        IPasswordService passwords,
        IAccessTokenGenerator accessTokenGenerator,
        IRefreshTokenGenerator refreshTokenGenerator,
        IUnitOfWork unitOfWork,
        IOptions<JwtOptions> jwtOptions,
        ILogger<AuthService> logger)
    {
        _users = users;
        _refreshTokens = refreshTokens;
        _passwords = passwords;
        _accessTokenGenerator = accessTokenGenerator;
        _refreshTokenGenerator = refreshTokenGenerator;
        _unitOfWork = unitOfWork;
        _jwtOptions = jwtOptions.Value;
        _logger = logger;
    }

    public async Task<AuthResponse> RegisterAsync(RegisterRequest request, string? ipAddress, CancellationToken cancellationToken = default)
    {
        if (await _users.ExistsByEmailAsync(request.Email, cancellationToken))
        {
            throw new ConflictException("An account with this email already exists.");
        }

        var user = new User
        {
            Email = request.Email.Trim().ToLowerInvariant(),
            DisplayName = request.DisplayName.Trim(),
            PasswordHash = _passwords.Hash(request.Password)
        };

        await _users.AddAsync(user, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var (refreshToken, plainRefreshToken) = await IssueRefreshTokenAsync(user.Id, ipAddress, familyId: null, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return BuildAuthResponse(user, refreshToken, plainRefreshToken);
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, string? ipAddress, CancellationToken cancellationToken = default)
    {
        var user = await _users.GetByEmailAsync(request.Email, cancellationToken)
            ?? throw new UnauthorizedException("Invalid email or password.");

        if (!_passwords.Verify(request.Password, user.PasswordHash))
        {
            throw new UnauthorizedException("Invalid email or password.");
        }

        var (refreshToken, plainRefreshToken) = await IssueRefreshTokenAsync(user.Id, ipAddress, familyId: null, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return BuildAuthResponse(user, refreshToken, plainRefreshToken);
    }

    public async Task<AuthResponse> RefreshAsync(RefreshTokenRequest request, string? ipAddress, CancellationToken cancellationToken = default)
    {
        var tokenHash = _refreshTokenGenerator.Hash(request.RefreshToken);
        var storedToken = await _refreshTokens.GetByHashAsync(tokenHash, cancellationToken);

        // Every failure below returns the same generic error on purpose: the caller must not be able
        // to tell an unknown token from an expired, revoked or replayed one.
        if (storedToken is null)
        {
            _logger.LogWarning("Refresh presented an unknown token from {Ip}", ipAddress);
            throw InvalidRefreshToken();
        }

        if (storedToken.IsUsed || storedToken.RevokedAt is not null)
        {
            // The token was already rotated. Presenting it again means the chain leaked, so the whole
            // account is treated as compromised and every session is revoked.
            _logger.LogWarning(
                "Refresh token reuse detected for user {UserId} in family {FamilyId} from {Ip}. Revoking all sessions.",
                storedToken.UserId, storedToken.FamilyId, ipAddress);

            await _refreshTokens.RevokeAllForUserAsync(storedToken.UserId, "refresh token reuse detected", cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            throw InvalidRefreshToken();
        }

        if (storedToken.ExpiresAt < DateTime.UtcNow)
        {
            _logger.LogWarning("Refresh token for user {UserId} in family {FamilyId} had expired", storedToken.UserId, storedToken.FamilyId);

            storedToken.RevokedAt = DateTime.UtcNow;
            storedToken.RevokedByIp = ipAddress;
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            throw InvalidRefreshToken();
        }

        var user = await _users.GetByIdAsync(storedToken.UserId, cancellationToken);
        if (user is null)
        {
            _logger.LogWarning("Refresh token references user {UserId} that no longer exists", storedToken.UserId);
            throw InvalidRefreshToken();
        }

        storedToken.IsUsed = true;
        storedToken.RevokedAt = DateTime.UtcNow;
        storedToken.RevokedByIp = ipAddress;

        var (newToken, newPlainToken) = await IssueRefreshTokenAsync(user.Id, ipAddress, storedToken.FamilyId, cancellationToken);
        storedToken.ReplacedByTokenHash = newToken.TokenHash;

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return BuildAuthResponse(user, newToken, newPlainToken);
    }

    public async Task LogoutAsync(Guid userId, LogoutRequest request, CancellationToken cancellationToken = default)
    {
        var tokenHash = _refreshTokenGenerator.Hash(request.RefreshToken);
        var storedToken = await _refreshTokens.GetByHashAsync(tokenHash, cancellationToken);

        // Unknown tokens and tokens owned by somebody else are both silently ignored: the endpoint
        // must not confirm whether a given token exists.
        if (storedToken is null)
        {
            return;
        }

        if (storedToken.UserId != userId)
        {
            _logger.LogWarning(
                "Logout refused: token of user {TokenOwnerId} presented by user {UserId}", storedToken.UserId, userId);
            return;
        }

        if (storedToken.RevokedAt is not null)
        {
            return;
        }

        storedToken.RevokedAt = DateTime.UtcNow;
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task ChangePasswordAsync(Guid userId, ChangePasswordRequest request, CancellationToken cancellationToken = default)
    {
        var user = await _users.GetByIdAsync(userId, cancellationToken)
            ?? throw new NotFoundException("User not found.");

        if (!_passwords.Verify(request.CurrentPassword, user.PasswordHash))
        {
            throw new BadRequestException("The current password is incorrect.");
        }

        user.PasswordHash = _passwords.Hash(request.NewPassword);
        await _refreshTokens.RevokeAllForUserAsync(userId, "password changed", cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private async Task<(RefreshToken Entity, string PlainToken)> IssueRefreshTokenAsync(
        Guid userId,
        string? ipAddress,
        Guid? familyId = null,
        CancellationToken cancellationToken = default)
    {
        var (plainToken, tokenHash) = _refreshTokenGenerator.Generate();
        var token = new RefreshToken
        {
            UserId = userId,
            TokenHash = tokenHash,
            // A login starts a new session family; a rotation inherits the family of its parent.
            FamilyId = familyId ?? Guid.CreateVersion7(),
            ExpiresAt = DateTime.UtcNow.AddDays(_jwtOptions.RefreshTokenLifetimeDays),
            CreatedByIp = ipAddress
        };

        await _refreshTokens.AddAsync(token, cancellationToken);
        return (token, plainToken);
    }

    private static UnauthorizedException InvalidRefreshToken()
        => new("Invalid refresh token.");

    private AuthResponse BuildAuthResponse(User user, RefreshToken refreshToken, string plainRefreshToken)
    {
        var lifetime = TimeSpan.FromMinutes(_jwtOptions.AccessTokenLifetimeMinutes);
        var accessToken = _accessTokenGenerator.Generate(user, lifetime);

        return new AuthResponse(
            accessToken,
            plainRefreshToken,
            (int)lifetime.TotalSeconds,
            "Bearer",
            new UserInfo(user.Id, user.Email, user.DisplayName));
    }
}
