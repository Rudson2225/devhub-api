using DevHub.Domain.Common;
using DevHub.Domain.Enums;

namespace DevHub.Domain.Entities;

public sealed class ActivityLog : BaseEntity
{
    public Guid ProjectId { get; set; }

    public Project Project { get; set; } = null!;

    public Guid UserId { get; set; }

    public User User { get; set; } = null!;

    public ActivityType Type { get; set; }

    public string Message { get; set; } = null!;

    public string? Data { get; set; }
}
