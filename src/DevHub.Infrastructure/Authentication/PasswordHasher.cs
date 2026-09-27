using Microsoft.AspNetCore.Identity;

namespace DevHub.Infrastructure.Authentication;

public sealed class PasswordHasher : IPasswordHasher<object>
{
    private readonly PasswordHasher<object> _inner = new();

    public string HashPassword(object user, string password)
        => _inner.HashPassword(user, password);

    public PasswordVerificationResult VerifyHashedPassword(object user, string hashedPassword, string providedPassword)
        => _inner.VerifyHashedPassword(user, hashedPassword, providedPassword);
}
