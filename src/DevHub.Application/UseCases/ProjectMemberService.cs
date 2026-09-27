using DevHub.Application.Abstractions;
using DevHub.Application.Common.Exceptions;
using DevHub.Application.DTOs.Projects;
using DevHub.Application.Mapping;
using DevHub.Domain;
using DevHub.Domain.Entities;
using DevHub.Domain.Enums;

namespace DevHub.Application.UseCases;

public interface IProjectMemberService
{
    Task<IReadOnlyList<ProjectMemberResponse>> ListAsync(Guid actorId, Guid projectId, CancellationToken cancellationToken = default);

    Task<ProjectMemberResponse> AddAsync(Guid actorId, Guid projectId, AddProjectMemberRequest request, CancellationToken cancellationToken = default);

    Task<ProjectMemberResponse> UpdateRoleAsync(Guid actorId, Guid projectId, Guid targetUserId, UpdateMemberRoleRequest request, CancellationToken cancellationToken = default);

    Task RemoveAsync(Guid actorId, Guid projectId, Guid targetUserId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RoleResponse>> ListRolesAsync(CancellationToken cancellationToken = default);
}

public sealed class ProjectMemberService : IProjectMemberService
{
    private readonly IProjectMemberRepository _members;
    private readonly IUserRepository _users;
    private readonly IRoleRepository _roles;
    private readonly IActivityRepository _activities;
    private readonly INotificationRepository _notifications;
    private readonly IProjectAccessService _access;
    private readonly IUnitOfWork _unitOfWork;

    public ProjectMemberService(
        IProjectMemberRepository members,
        IUserRepository users,
        IRoleRepository roles,
        IActivityRepository activities,
        INotificationRepository notifications,
        IProjectAccessService access,
        IUnitOfWork unitOfWork)
    {
        _members = members;
        _users = users;
        _roles = roles;
        _activities = activities;
        _notifications = notifications;
        _access = access;
        _unitOfWork = unitOfWork;
    }

    public async Task<IReadOnlyList<ProjectMemberResponse>> ListAsync(Guid actorId, Guid projectId, CancellationToken cancellationToken = default)
    {
        await _access.EnsurePermissionAsync(projectId, Permissions.ProjectRead, actorId, cancellationToken);

        // Reading the roster and reading contact details are two different things. Anyone in the
        // project needs the display name and the role to collaborate, but only somebody who may
        // administer the membership has a use for the addresses behind it; without that permission
        // the row is returned with a null email, apart from the caller's own.
        var canSeeContactDetails = await _access.HasPermissionAsync(
            projectId, Permissions.MembersManage, actorId, cancellationToken);

        var members = await _members.ListMembersAsync(projectId, cancellationToken);
        return members
            .OrderBy(m => m.Role.Name)
            .ThenBy(m => m.User.DisplayName)
            .Select(m => m.ToResponse())
            .Select(m => canSeeContactDetails || m.UserId == actorId ? m : m with { Email = null })
            .ToList();
    }

    public async Task<ProjectMemberResponse> AddAsync(Guid actorId, Guid projectId, AddProjectMemberRequest request, CancellationToken cancellationToken = default)
    {
        await _access.EnsurePermissionAsync(projectId, Permissions.MembersManage, actorId, cancellationToken);

        var user = await _users.GetByEmailAsync(request.Email, cancellationToken)
            ?? throw new NotFoundException($"No user found with email '{request.Email}'.");

        var existing = await _members.GetMembershipAsync(projectId, user.Id, cancellationToken);
        if (existing is not null)
        {
            throw new ConflictException("This user is already a member of the project.");
        }

        var role = await _roles.GetByNameAsync(request.Role, cancellationToken)
            ?? throw new ConflictException($"Role '{request.Role}' is not configured.");

        var member = new ProjectMember
        {
            ProjectId = projectId,
            UserId = user.Id,
            RoleId = role.Id,
            JoinedAt = DateTime.UtcNow
        };

        await _members.AddAsync(member, cancellationToken);
        await _activities.AddAsync(new ActivityLog
        {
            ProjectId = projectId,
            UserId = actorId,
            Type = ActivityType.MemberAdded,
            Message = $"{user.DisplayName} was added to the project as {role.Name}."
        }, cancellationToken);

        await _notifications.AddAsync(new Notification
        {
            UserId = user.Id,
            ProjectId = projectId,
            Type = NotificationType.MemberAdded,
            Title = "You were added to a project",
            Message = $"You were added to the project as {role.Name}."
        }, cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // Refresh the navigations so the response includes user + role details.
        var saved = await _members.GetMembershipAsync(projectId, user.Id, cancellationToken)
            ?? throw new NotFoundException("Member not found.");
        return saved.ToResponse();
    }

    public async Task<ProjectMemberResponse> UpdateRoleAsync(Guid actorId, Guid projectId, Guid targetUserId, UpdateMemberRoleRequest request, CancellationToken cancellationToken = default)
    {
        await _access.EnsurePermissionAsync(projectId, Permissions.MembersManage, actorId, cancellationToken);

        var member = await _members.GetMembershipAsync(projectId, targetUserId, cancellationToken)
            ?? throw new NotFoundException("The target user is not a member of this project.");

        if (member.Role.Name == Role.Owner)
        {
            throw new BadRequestException("The role of the project owner cannot be changed.");
        }

        var newRole = await _roles.GetByNameAsync(request.Role, cancellationToken)
            ?? throw new ConflictException($"Role '{request.Role}' is not configured.");

        if (member.RoleId == newRole.Id)
        {
            throw new ConflictException($"The user already has the '{request.Role}' role.");
        }

        var oldRoleName = member.Role.Name;
        member.RoleId = newRole.Id;

        await _activities.AddAsync(new ActivityLog
        {
            ProjectId = projectId,
            UserId = actorId,
            Type = ActivityType.MemberRoleChanged,
            Message = $"{member.User.DisplayName}'s role was changed from {oldRoleName} to {newRole.Name}."
        }, cancellationToken);

        await _notifications.AddAsync(new Notification
        {
            UserId = targetUserId,
            ProjectId = projectId,
            Type = NotificationType.RoleChanged,
            Title = "Your role changed",
            Message = $"Your role in the project was changed from {oldRoleName} to {newRole.Name}."
        }, cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var updated = await _members.GetMembershipAsync(projectId, targetUserId, cancellationToken)
            ?? throw new NotFoundException("Member not found.");
        return updated.ToResponse();
    }

    public async Task RemoveAsync(Guid actorId, Guid projectId, Guid targetUserId, CancellationToken cancellationToken = default)
    {
        await _access.EnsurePermissionAsync(projectId, Permissions.MembersManage, actorId, cancellationToken);

        var member = await _members.GetMembershipAsync(projectId, targetUserId, cancellationToken)
            ?? throw new NotFoundException("The target user is not a member of this project.");

        if (member.Role.Name == Role.Owner)
        {
            throw new BadRequestException("The project owner cannot be removed from the project.");
        }

        _members.Remove(member);

        await _activities.AddAsync(new ActivityLog
        {
            ProjectId = projectId,
            UserId = actorId,
            Type = ActivityType.MemberRemoved,
            Message = $"{member.User.DisplayName} was removed from the project."
        }, cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<RoleResponse>> ListRolesAsync(CancellationToken cancellationToken = default)
    {
        var roles = await _roles.ListWithPermissionsAsync(cancellationToken);
        return roles
            .OrderBy(r => r.Name)
            .Select(r => r.ToResponse())
            .ToList();
    }
}
