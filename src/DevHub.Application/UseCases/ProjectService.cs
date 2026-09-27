using DevHub.Application.Abstractions;
using DevHub.Application.Common;
using DevHub.Application.Common.Exceptions;
using DevHub.Application.DTOs.Projects;
using DevHub.Application.Mapping;
using DevHub.Domain;
using DevHub.Domain.Entities;
using DevHub.Domain.Enums;

namespace DevHub.Application.UseCases;

public interface IProjectService
{
    Task<ProjectResponse> CreateAsync(Guid actorId, CreateProjectRequest request, CancellationToken cancellationToken = default);

    Task<PagedResult<ProjectSummaryResponse>> ListAsync(Guid actorId, int page, int pageSize, CancellationToken cancellationToken = default);

    Task<ProjectResponse> GetAsync(Guid actorId, Guid projectId, CancellationToken cancellationToken = default);

    Task<ProjectResponse> UpdateAsync(Guid actorId, Guid projectId, UpdateProjectRequest request, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid actorId, Guid projectId, CancellationToken cancellationToken = default);
}

public sealed class ProjectService : IProjectService
{
    private readonly IProjectRepository _projects;
    private readonly IProjectMemberRepository _members;
    private readonly ITaskRepository _tasks;
    private readonly IRoleRepository _roles;
    private readonly IActivityRepository _activities;
    private readonly IProjectAccessService _access;
    private readonly IUnitOfWork _unitOfWork;

    public ProjectService(
        IProjectRepository projects,
        IProjectMemberRepository members,
        ITaskRepository tasks,
        IRoleRepository roles,
        IActivityRepository activities,
        IProjectAccessService access,
        IUnitOfWork unitOfWork)
    {
        _projects = projects;
        _members = members;
        _tasks = tasks;
        _roles = roles;
        _activities = activities;
        _access = access;
        _unitOfWork = unitOfWork;
    }

    public async Task<ProjectResponse> CreateAsync(Guid actorId, CreateProjectRequest request, CancellationToken cancellationToken = default)
    {
        var ownerRole = await FindRoleAsync(Role.Owner, cancellationToken);

        var project = new Project
        {
            Name = request.Name.Trim(),
            Description = request.Description,
            Status = request.Status ?? ProjectStatus.Active,
            OwnerId = actorId
        };

        var membership = new ProjectMember
        {
            ProjectId = project.Id,
            UserId = actorId,
            RoleId = ownerRole.Id,
            JoinedAt = DateTime.UtcNow
        };

        await _projects.AddAsync(project, cancellationToken);
        await _members.AddAsync(membership, cancellationToken);
        await LogActivityAsync(project.Id, actorId, ActivityType.ProjectCreated, $"Project '{project.Name}' was created.", cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var saved = await _projects.GetByIdAsync(project.Id, cancellationToken) ?? project;
        return saved.ToResponse(memberCount: 1, taskCount: 0);
    }

    public async Task<PagedResult<ProjectSummaryResponse>> ListAsync(Guid actorId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var pagination = PaginationQuery.Normalize(page, pageSize);
        var projects = await _projects.GetUserProjectsAsync(actorId, cancellationToken);
        var items = projects
            .OrderByDescending(p => p.CreatedAt)
            .Skip((pagination.Page - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .Select(p => p.ToSummaryResponse())
            .ToList();

        return PagedResult<ProjectSummaryResponse>.Create(items, pagination.Page, pagination.PageSize, projects.Count);
    }

    public async Task<ProjectResponse> GetAsync(Guid actorId, Guid projectId, CancellationToken cancellationToken = default)
    {
        var project = await _projects.GetByIdAsync(projectId, cancellationToken)
            ?? throw new NotFoundException("Project not found.");

        await _access.EnsurePermissionAsync(projectId, Permissions.ProjectRead, actorId, cancellationToken);

        var memberCount = await _members.CountAsync(projectId, cancellationToken);
        var taskCount = await _tasks.CountAllAsync(projectId, cancellationToken);

        return project.ToResponse(memberCount, taskCount);
    }

    public async Task<ProjectResponse> UpdateAsync(Guid actorId, Guid projectId, UpdateProjectRequest request, CancellationToken cancellationToken = default)
    {
        await _access.EnsurePermissionAsync(projectId, Permissions.ProjectUpdate, actorId, cancellationToken);

        var project = await _projects.GetByIdAsync(projectId, cancellationToken)
            ?? throw new NotFoundException("Project not found.");

        if (request.Name is not null)
        {
            project.Name = request.Name.Trim();
        }

        if (request.Description is not null)
        {
            project.Description = request.Description;
        }

        if (request.Status is not null)
        {
            project.Status = request.Status.Value;
        }

        await LogActivityAsync(projectId, actorId, ActivityType.ProjectUpdated, $"Project '{project.Name}' was updated.", cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var memberCount = await _members.CountAsync(projectId, cancellationToken);
        var taskCount = await _tasks.CountAllAsync(projectId, cancellationToken);

        return project.ToResponse(memberCount, taskCount);
    }

    public async Task DeleteAsync(Guid actorId, Guid projectId, CancellationToken cancellationToken = default)
    {
        await _access.EnsurePermissionAsync(projectId, Permissions.ProjectDelete, actorId, cancellationToken);

        var project = await _projects.GetByIdAsync(projectId, cancellationToken)
            ?? throw new NotFoundException("Project not found.");

        // Nothing else has to read the project: its activity log is cascade-deleted with it.
        _projects.Remove(project);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private async Task<Role> FindRoleAsync(string roleName, CancellationToken cancellationToken)
    {
        if (await _roles.GetByNameAsync(roleName, cancellationToken) is { } role)
        {
            return role;
        }

        throw new ConflictException($"Role '{roleName}' is not configured. Run database seeding first.");
    }

    private async Task LogActivityAsync(Guid projectId, Guid actorId, ActivityType type, string message, CancellationToken cancellationToken)
    {
        await _activities.AddAsync(new ActivityLog
        {
            ProjectId = projectId,
            UserId = actorId,
            Type = type,
            Message = message
        }, cancellationToken);
    }
}
