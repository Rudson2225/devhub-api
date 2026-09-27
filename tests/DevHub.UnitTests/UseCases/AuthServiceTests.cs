using DevHub.Application.Common.Exceptions;
using DevHub.Application.DTOs.Auth;
using DevHub.Application.Options;
using DevHub.Application.UseCases;
using DevHub.Domain.Entities;
using DevHub.Infrastructure.Authentication;
using DevHub.UnitTests.Fakes;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DevHub.UnitTests.UseCases;

public sealed class AuthServiceTests
{
    private readonly FakeUserRepository _users = new();
    private readonly FakeRefreshTokenRepository _tokens = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly AuthService _service;

    public AuthServiceTests()
    {
        var jwtOptions = Options.Create(new JwtOptions
        {
            Issuer = "DevHub",
            Audience = "DevHub.Client",
            SecretKey = "test-secret-key-with-at-least-32-bytes!!",
            AccessTokenLifetimeMinutes = 15,
            RefreshTokenLifetimeDays = 7
        });

        _service = new AuthService(
            _users,
            _tokens,
            new PasswordService(),
            new FakeAccessTokenGenerator(),
            new RefreshTokenGenerator(),
            _unitOfWork,
            jwtOptions,
            NullLogger<AuthService>.Instance);
    }

    [Fact]
    public async Task Register_creates_user_and_persists_refresh_token()
    {
        var response = await _service.RegisterAsync(
            new RegisterRequest("dev@example.com", "Str0ng!Pass123", "Dev"),
            "192.168.0.1");

        response.AccessToken.Should().NotBeNullOrEmpty();
        response.RefreshToken.Should().NotBeNullOrEmpty();
        response.User.Email.Should().Be("dev@example.com");

        _users.Users.Should().HaveCount(1);
        _tokens.Tokens.Should().HaveCount(1);
        _tokens.Tokens[0].UserId.Should().Be(response.User.Id);
        response.RefreshToken.Should().NotBe(_tokens.Tokens[0].TokenHash);
        _unitOfWork.SaveCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Register_with_existing_email_throws_conflict()
    {
        _users.Users.Add(TestUsers.NewUser("dev@example.com"));

        var act = () => _service.RegisterAsync(
            new RegisterRequest("DEV@EXAMPLE.COM", "Str0ng!Pass123", "Dev"),
            null);

        await act.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task Login_with_valid_credentials_succeeds_and_verifies_password()
    {
        var passwordHash = new PasswordService().Hash("Str0ng!Pass123");
        _users.Users.Add(new User
        {
            Id = Guid.NewGuid(),
            Email = "dev@example.com",
            DisplayName = "Dev",
            PasswordHash = passwordHash
        });

        var response = await _service.LoginAsync(
            new LoginRequest("dev@example.com", "Str0ng!Pass123"),
            null);

        response.User.Email.Should().Be("dev@example.com");
        _tokens.Tokens.Should().HaveCount(1);
    }

    [Fact]
    public async Task Login_with_wrong_password_throws_unauthorized()
    {
        _users.Users.Add(new User
        {
            Id = Guid.NewGuid(),
            Email = "dev@example.com",
            DisplayName = "Dev",
            PasswordHash = new PasswordService().Hash("Str0ng!Pass123")
        });

        var act = () => _service.LoginAsync(
            new LoginRequest("dev@example.com", "Wrong!Pass123"),
            null);

        await act.Should().ThrowAsync<UnauthorizedException>();
    }

    [Fact]
    public async Task Refresh_rotates_token_and_invalidates_previous()
    {
        var register = await _service.RegisterAsync(
            new RegisterRequest("dev@example.com", "Str0ng!Pass123", "Dev"),
            null);
        var oldHash = RefreshTokenGeneratorHash(register.RefreshToken);

        var refresh = await _service.RefreshAsync(
            new RefreshTokenRequest(register.RefreshToken),
            "10.0.0.1");

        refresh.RefreshToken.Should().NotBe(register.RefreshToken);

        _tokens.Tokens.Should().HaveCount(2);
        var old = _tokens.Tokens.Single(t => t.TokenHash == oldHash);
        old.IsUsed.Should().BeTrue();
        old.RevokedAt.Should().NotBeNull();
        old.ReplacedByTokenHash.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Refresh_with_used_token_throws_unauthorized()
    {
        var register = await _service.RegisterAsync(
            new RegisterRequest("dev@example.com", "Str0ng!Pass123", "Dev"),
            null);

        await _service.RefreshAsync(new RefreshTokenRequest(register.RefreshToken), null);

        var act = () => _service.RefreshAsync(new RefreshTokenRequest(register.RefreshToken), null);
        await act.Should().ThrowAsync<UnauthorizedException>();
    }

    [Fact]
    public async Task Refresh_with_expired_token_throws_unauthorized()
    {
        var register = await _service.RegisterAsync(
            new RegisterRequest("dev@example.com", "Str0ng!Pass123", "Dev"),
            null);

        var stored = _tokens.Tokens.Single();
        stored.IsUsed = false;
        stored.ExpiresAt = DateTime.UtcNow.AddMinutes(-1);

        var act = () => _service.RefreshAsync(new RefreshTokenRequest(register.RefreshToken), null);
        await act.Should().ThrowAsync<UnauthorizedException>();
    }

    [Fact]
    public async Task Logout_revokes_the_refresh_token()
    {
        var register = await _service.RegisterAsync(
            new RegisterRequest("dev@example.com", "Str0ng!Pass123", "Dev"),
            null);
        var userId = _users.Users.Single().Id;

        await _service.LogoutAsync(userId, new LogoutRequest(register.RefreshToken));

        _tokens.Tokens.Single().RevokedAt.Should().NotBeNull();

        var act = () => _service.RefreshAsync(new RefreshTokenRequest(register.RefreshToken), null);
        await act.Should().ThrowAsync<UnauthorizedException>();
    }

    [Fact]
    public async Task Logout_by_another_user_does_not_revoke_the_token()
    {
        var register = await _service.RegisterAsync(
            new RegisterRequest("dev@example.com", "Str0ng!Pass123", "Dev"),
            null);
        var attackerId = Guid.NewGuid();

        await _service.LogoutAsync(attackerId, new LogoutRequest(register.RefreshToken));

        _tokens.Tokens.Single().RevokedAt.Should().BeNull("ownership must be verified before revoking");

        // The rightful owner can still use the session.
        var refresh = await _service.RefreshAsync(new RefreshTokenRequest(register.RefreshToken), null);
        refresh.RefreshToken.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ChangePassword_with_wrong_current_password_throws()
    {
        await _service.RegisterAsync(
            new RegisterRequest("dev@example.com", "Str0ng!Pass123", "Dev"),
            null);
        var userId = _users.Users.Single().Id;

        var act = () => _service.ChangePasswordAsync(
            userId,
            new ChangePasswordRequest("Wrong!Pass123", "NewStr0ng!Pass123"));

        await act.Should().ThrowAsync<BadRequestException>();
    }

    [Fact]
    public async Task ChangePassword_updates_hash_and_revokes_all_refresh_tokens()
    {
        var register = await _service.RegisterAsync(
            new RegisterRequest("dev@example.com", "Str0ng!Pass123", "Dev"),
            null);
        var userId = _users.Users.Single().Id;
        var oldHash = _users.Users.Single().PasswordHash;

        await _service.ChangePasswordAsync(
            userId,
            new ChangePasswordRequest("Str0ng!Pass123", "NewStr0ng!Pass123"));

        _users.Users.Single().PasswordHash.Should().NotBe(oldHash);
        _tokens.Tokens.Single().RevokedAt.Should().NotBeNull();

        var act = () => _service.RefreshAsync(new RefreshTokenRequest(register.RefreshToken), null);
        await act.Should().ThrowAsync<UnauthorizedException>();
    }

    [Fact]
    public async Task Change_password_records_the_reason_and_never_writes_it_as_an_address()
    {
        await _service.RegisterAsync(
            new RegisterRequest("dev@example.com", "Str0ng!Pass123", "Dev"),
            "10.0.0.1");
        var userId = _users.Users.Single().Id;

        await _service.ChangePasswordAsync(
            userId,
            new ChangePasswordRequest("Str0ng!Pass123", "NewStr0ng!Pass123"));

        var token = _tokens.Tokens.Single();
        token.RevokedAt.Should().NotBeNull();
        token.RevokedReason.Should().Be("password changed");
        token.RevokedByIp.Should().BeNull("a bulk revocation has a reason, not a caller address");
    }

    [Fact]
    public async Task Reuse_detection_revokes_every_session_with_the_reason_recorded()
    {
        var register = await _service.RegisterAsync(
            new RegisterRequest("dev@example.com", "Str0ng!Pass123", "Dev"),
            null);

        // A second, independent session for the same account.
        await _service.LoginAsync(new LoginRequest("dev@example.com", "Str0ng!Pass123"), "10.0.0.2");
        _tokens.Tokens.Should().HaveCount(2);

        var rotated = await _service.RefreshAsync(new RefreshTokenRequest(register.RefreshToken), "10.0.0.3");
        _tokens.Tokens.Should().HaveCount(3);

        var act = () => _service.RefreshAsync(new RefreshTokenRequest(register.RefreshToken), "10.0.0.4");
        await act.Should().ThrowAsync<UnauthorizedException>();

        // Everything is revoked: the two sessions that were still live carry the reason, and the token
        // that had already been rotated keeps the address that performed the rotation.
        _tokens.Tokens.Should().OnlyContain(t => t.RevokedAt != null);
        _tokens.Tokens.Count(t => t.RevokedReason == "refresh token reuse detected").Should().Be(2);
        _tokens.Tokens.Should().NotContain(t => t.RevokedByIp == "refresh token reuse detected",
            "the reason belongs in RevokedReason, not in the address column");

        // The other session was killed too, which is the whole point of treating the account as compromised.
        var otherSession = () => _service.RefreshAsync(new RefreshTokenRequest(rotated.RefreshToken), "10.0.0.4");
        await otherSession.Should().ThrowAsync<UnauthorizedException>();
    }

    [Fact]
    public async Task Rotating_a_token_records_the_rotating_address_separately_from_the_reason()
    {
        var register = await _service.RegisterAsync(
            new RegisterRequest("dev@example.com", "Str0ng!Pass123", "Dev"),
            "10.0.0.1");
        var oldHash = RefreshTokenGeneratorHash(register.RefreshToken);

        await _service.RefreshAsync(new RefreshTokenRequest(register.RefreshToken), "10.0.0.2");

        var old = _tokens.Tokens.Single(t => t.TokenHash == oldHash);
        old.RevokedByIp.Should().Be("10.0.0.2");
        old.RevokedReason.Should().BeNull("a plain rotation is not a revocation reason");
        old.CreatedAt.Should().BeBefore(old.ExpiresAt);
    }

    private static string RefreshTokenGeneratorHash(string plain)
        => new RefreshTokenGenerator().Hash(plain);
}
