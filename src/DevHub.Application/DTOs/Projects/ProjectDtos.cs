using DevHub.Domain.Entities;
using DevHub.Domain.Enums;

namespace DevHub.Application.DTOs.Projects;

public sealed record CreateProjectRequest(string Name, string? Description, ProjectStatus? Status);

public sealed record UpdateProjectRequest(string? Name, string? Description, ProjectStatus? Status);

public sealed record ProjectResponse(
    Guid Id,
    string Name,
    string? Description,
    ProjectStatus Status,
    Guid OwnerId,
    string OwnerName,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    int MemberCount,
    int TaskCount);

public sealed record ProjectSummaryResponse(
    Guid Id,
    string Name,
    string? Description,
    ProjectStatus Status,
    Guid OwnerId,
    DateTime CreatedAt);

/// <summary>
/// One row of the project member list.
/// <para>
/// <c>Email</c> is a contact detail: it is only filled in for callers holding <c>members.manage</c>,
/// or for the caller's own row, so the least privileged role cannot harvest the addresses of the
/// whole project.
/// </para>
/// </summary>
public sealed record ProjectMemberResponse(
    Guid UserId,
    string? Email,
    string DisplayName,
    string? AvatarUrl,
    string Role,
    DateTime JoinedAt);

public sealed record AddProjectMemberRequest(string Email, string Role);

public sealed record UpdateMemberRoleRequest(string Role);

public sealed record RoleResponse(Guid Id, string Name, string? Description, IReadOnlyList<PermissionResponse> Permissions);

public sealed record PermissionResponse(Guid Id, string Name, string? Description);
