using DevHub.Domain.Common;
using DevHub.Domain.Enums;

namespace DevHub.Domain.Entities;

public sealed class Project : BaseEntity
{
    public const int MaxNameLength = 100;
    public const int MaxDescriptionLength = 2000;

    public string Name { get; set; } = null!;

    public string? Description { get; set; }

    public ProjectStatus Status { get; set; } = ProjectStatus.Active;

    public Guid OwnerId { get; set; }

    public User Owner { get; set; } = null!;

    public ICollection<ProjectMember> Members { get; set; } = new List<ProjectMember>();

    public ICollection<ProjectTag> Tags { get; set; } = new List<ProjectTag>();

    public ICollection<ProjectTask> Tasks { get; set; } = new List<ProjectTask>();

    public ICollection<Document> Documents { get; set; } = new List<Document>();

    public ICollection<ActivityLog> Activities { get; set; } = new List<ActivityLog>();
}
