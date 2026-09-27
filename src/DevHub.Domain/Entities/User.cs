using DevHub.Domain.Common;

namespace DevHub.Domain.Entities;

public sealed class User : BaseEntity
{
    public string Email { get; set; } = null!;

    public string PasswordHash { get; set; } = null!;

    public string DisplayName { get; set; } = null!;

    public string? Bio { get; set; }

    public string? AvatarUrl { get; set; }

    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();

    public ICollection<Project> OwnedProjects { get; set; } = new List<Project>();

    public ICollection<ProjectMember> Memberships { get; set; } = new List<ProjectMember>();

    public ICollection<ProjectTask> CreatedTasks { get; set; } = new List<ProjectTask>();

    public ICollection<ProjectTask> AssignedTasks { get; set; } = new List<ProjectTask>();

    public ICollection<TaskComment> Comments { get; set; } = new List<TaskComment>();

    public ICollection<Document> Documents { get; set; } = new List<Document>();

    public ICollection<ActivityLog> Activities { get; set; } = new List<ActivityLog>();

    public ICollection<Notification> Notifications { get; set; } = new List<Notification>();
}
