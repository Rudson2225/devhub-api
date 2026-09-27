using System.Security.Cryptography;
using System.Text;
using DevHub.Application.Abstractions;

namespace DevHub.Infrastructure.Authentication;

public sealed class RefreshTokenGenerator : IRefreshTokenGenerator
{
    public (string PlainToken, string TokenHash) Generate()
    {
        var plainToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        return (plainToken, Hash(plainToken));
    }

    public string Hash(string plainToken)
        => Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(plainToken)));
}
