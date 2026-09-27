using DevHub.Domain.Entities;

namespace DevHub.Application.Abstractions;

public interface IPasswordService
{
    string Hash(string password);

    bool Verify(string password, string passwordHash);
}

public interface IAccessTokenGenerator
{
    string Generate(User user, TimeSpan lifetime);
}

public interface IRefreshTokenGenerator
{
    (string PlainToken, string TokenHash) Generate();

    string Hash(string plainToken);
}
