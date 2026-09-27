using DevHub.Application.Abstractions;
using DevHub.Application.Common.Exceptions;
using DevHub.Domain.Enums;

namespace DevHub.Application.UseCases;

public sealed class ProjectAccessService : IProjectAccessService
{
    private readonly IProjectMemberRepository _members;
    private readonly IPermissionRepository _permissions;

    public ProjectAccessService(IProjectMemberRepository members, IPermissionRepository permissions)
    {
        _members = members;
        _permissions = permissions;
    }

    public async Task EnsureProjectMemberAsync(Guid projectId, Guid actorId, CancellationToken cancellationToken = default)
    {
        var membership = await _members.GetMembershipAsync(projectId, actorId, cancellationToken)
            ?? throw new ForbiddenException("You do not have access to this project.");
    }

    public async Task EnsurePermissionAsync(Guid projectId, string permission, Guid actorId, CancellationToken cancellationToken = default)
    {
        var membership = await _members.GetMembershipAsync(projectId, actorId, cancellationToken);
        if (membership is null)
        {
            throw new ForbiddenException("You do not have access to this project.");
        }

        if (!await HasPermissionAsync(membership.RoleId, permission, cancellationToken))
        {
            throw new ForbiddenException($"You do not have the '{permission}' permission for this project.");
        }
    }

    public async Task<bool> HasPermissionAsync(Guid projectId, string permission, Guid actorId, CancellationToken cancellationToken = default)
    {
        var membership = await _members.GetMembershipAsync(projectId, actorId, cancellationToken);
        return membership is not null
            && await HasPermissionAsync(membership.RoleId, permission, cancellationToken);
    }

    private async Task<bool> HasPermissionAsync(Guid roleId, string permission, CancellationToken cancellationToken)
    {
        var permissions = await _permissions.GetPermissionsForRolesAsync([roleId], cancellationToken);
        return permissions.Contains(permission);
    }

    public async Task<ProjectMemberRole?> GetRoleAsync(Guid projectId, Guid actorId, CancellationToken cancellationToken = default)
    {
        var membership = await _members.GetMembershipAsync(projectId, actorId, cancellationToken);
        if (membership is null)
        {
            return null;
        }

        return Enum.TryParse<ProjectMemberRole>(membership.Role.Name, ignoreCase: true, out var role)
            ? role
            : null;
    }
}
