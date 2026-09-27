using DevHub.Domain.Common;
using DevHub.Domain.Enums;

namespace DevHub.Domain.Entities;

public sealed class ProjectTask : BaseEntity
{
    public const int MaxTitleLength = 200;
    public const int MaxDescriptionLength = 5000;

    public Guid ProjectId { get; set; }

    public Project Project { get; set; } = null!;

    public string Title { get; set; } = null!;

    public string? Description { get; set; }

    public ProjectTaskStatus Status { get; set; } = ProjectTaskStatus.Backlog;

    public ProjectTaskPriority Priority { get; set; } = ProjectTaskPriority.Medium;

    public Guid? AssigneeId { get; set; }

    public User? Assignee { get; set; }

    public Guid ReporterId { get; set; }

    public User Reporter { get; set; } = null!;

    public DateTime? DueDate { get; set; }

    public DateTime? CompletedAt { get; set; }

    public ICollection<TaskComment> Comments { get; set; } = new List<TaskComment>();

    public ICollection<TaskTag> Tags { get; set; } = new List<TaskTag>();
}
