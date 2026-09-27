using DevHub.Domain.Common;

namespace DevHub.Domain.Entities;

public sealed class Tag : BaseEntity
{
    public const int MaxNameLength = 50;

    public Guid? ProjectId { get; set; }

    public Project? Project { get; set; }

    public string Name { get; set; } = null!;

    public string? Description { get; set; }

    public string Color { get; set; } = "#6b7280";

    public ICollection<ProjectTag> Projects { get; set; } = new List<ProjectTag>();

    public ICollection<TaskTag> Tasks { get; set; } = new List<TaskTag>();
}
