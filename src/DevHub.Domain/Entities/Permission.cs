using DevHub.Domain.Common;

namespace DevHub.Domain.Entities;

public sealed class Permission : BaseEntity
{
    public string Name { get; set; } = null!;

    public string? Description { get; set; }

    public ICollection<RolePermission> Roles { get; set; } = new List<RolePermission>();
}
