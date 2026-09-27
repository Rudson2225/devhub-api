namespace DevHub.Domain.Entities;

public sealed class TaskTag
{
    public Guid TaskId { get; set; }

    public ProjectTask Task { get; set; } = null!;

    public Guid TagId { get; set; }

    public Tag Tag { get; set; } = null!;
}
