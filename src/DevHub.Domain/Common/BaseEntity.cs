namespace DevHub.Domain.Common;

public abstract class BaseEntity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    protected BaseEntity()
    {
        var now = DateTime.UtcNow;
        CreatedAt = now;
        UpdatedAt = now;
    }
}
