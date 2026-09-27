using DevHub.Domain.Common;

namespace DevHub.Domain.Entities;

public sealed class TaskComment : BaseEntity
{
    public const int MaxContentLength = 2000;

    public Guid TaskId { get; set; }

    public ProjectTask Task { get; set; } = null!;

    public Guid AuthorId { get; set; }

    public User Author { get; set; } = null!;

    public Guid ProjectId { get; set; }

    public Project Project { get; set; } = null!;

    public string Content { get; set; } = null!;
}
