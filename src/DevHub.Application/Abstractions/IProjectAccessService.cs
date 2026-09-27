using DevHub.Domain.Enums;

namespace DevHub.Application.Abstractions;

public interface IProjectAccessService
{
    Task EnsurePermissionAsync(Guid projectId, string permission, Guid actorId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Non-throwing variant, for callers that branch on the answer instead of refusing the request.
    /// </summary>
    Task<bool> HasPermissionAsync(Guid projectId, string permission, Guid actorId, CancellationToken cancellationToken = default);

    Task EnsureProjectMemberAsync(Guid projectId, Guid actorId, CancellationToken cancellationToken = default);

    Task<ProjectMemberRole?> GetRoleAsync(Guid projectId, Guid actorId, CancellationToken cancellationToken = default);
}
