namespace DevHub.Application.Abstractions;

public interface ICurrentUserService
{
    /// <summary>
    /// The user the validated access token was issued for, or null when the request is anonymous or
    /// the subject claim is missing or malformed. Read it from here rather than from the body or a
    /// route value: everything an authorization decision depends on has to come from the token.
    /// </summary>
    Guid? UserId { get; }
}
