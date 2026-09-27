using DevHub.Domain.Common;

namespace DevHub.Domain.Entities;

public sealed class Role : BaseEntity
{
    public const string Owner = "Owner";
    public const string Admin = "Admin";
    public const string Member = "Member";
    public const string Viewer = "Viewer";

    public string Name { get; set; } = null!;

    public string? Description { get; set; }

    public ICollection<RolePermission> Permissions { get; set; } = new List<RolePermission>();

    public ICollection<ProjectMember> Members { get; set; } = new List<ProjectMember>();
}
