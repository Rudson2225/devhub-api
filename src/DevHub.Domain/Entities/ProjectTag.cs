namespace DevHub.Domain.Entities;

public sealed class ProjectTag
{
    public Guid ProjectId { get; set; }

    public Project Project { get; set; } = null!;

    public Guid TagId { get; set; }

    public Tag Tag { get; set; } = null!;

    public DateTime AddedAt { get; set; }
}
