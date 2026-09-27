namespace DevHub.Domain.Entities;

public sealed class RefreshToken
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid UserId { get; set; }

    public User User { get; set; } = null!;

    public string TokenHash { get; set; } = null!;

    /// <summary>
    /// Identifies the session this token belongs to. The first token of a login starts a family and
    /// every rotated descendant inherits it, so a chain can always be traced back to its origin.
    /// </summary>
    public Guid FamilyId { get; set; } = Guid.CreateVersion7();

    public DateTime CreatedAt { get; set; }

    public DateTime ExpiresAt { get; set; }

    public bool IsUsed { get; set; }

    public DateTime? RevokedAt { get; set; }

    public string? ReplacedByTokenHash { get; set; }

    public string? CreatedByIp { get; set; }

    public string? RevokedByIp { get; set; }

    public RefreshToken()
    {
        CreatedAt = DateTime.UtcNow;
    }
}
