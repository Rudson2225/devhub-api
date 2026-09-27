using DevHub.Domain.Common;
using DevHub.Domain.Enums;

namespace DevHub.Domain.Entities;

public sealed class Notification : BaseEntity
{
    public Guid UserId { get; set; }

    public User User { get; set; } = null!;

    public Guid? ProjectId { get; set; }

    public Project? Project { get; set; }

    public NotificationType Type { get; set; }

    public string Title { get; set; } = null!;

    public string Message { get; set; } = null!;

    public string? Data { get; set; }

    public bool IsRead { get; set; }

    public DateTime? ReadAt { get; set; }
}
