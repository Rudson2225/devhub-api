using DevHub.Application.Abstractions;
using DevHub.Application.Common.Exceptions;
using DevHub.Application.DTOs.Dashboard;
using DevHub.Domain;
using DevHub.Domain.Entities;
using DevHub.Domain.Enums;

namespace DevHub.Application.UseCases;

public interface IDashboardService
{
    Task<ProjectDashboardResponse> GetProjectDashboardAsync(Guid actorId, Guid projectId, CancellationToken cancellationToken = default);
}

public sealed class DashboardService : IDashboardService
{
    private readonly ITaskRepository _tasks;
    private readonly ITaskCommentRepository _comments;
    private readonly IProjectMemberRepository _members;
    private readonly IActivityRepository _activities;
    private readonly IProjectAccessService _access;

    public DashboardService(
        ITaskRepository tasks,
        ITaskCommentRepository comments,
        IProjectMemberRepository members,
        IActivityRepository activities,
        IProjectAccessService access)
    {
        _tasks = tasks;
        _comments = comments;
        _members = members;
        _activities = activities;
        _access = access;
    }

    public async Task<ProjectDashboardResponse> GetProjectDashboardAsync(Guid actorId, Guid projectId, CancellationToken cancellationToken = default)
    {
        await _access.EnsurePermissionAsync(projectId, Permissions.DashboardRead, actorId, cancellationToken);

        var statusCounts = await _tasks.CountByStatusAsync(projectId, cancellationToken);
        var priorityCounts = await _tasks.CountByPriorityAsync(projectId, cancellationToken);
        var criticalCount = await _tasks.CountCriticalAsync(projectId, cancellationToken);
        var memberCount = await _members.CountAsync(projectId, cancellationToken);
        var recentActivities = await _activities.ListAsync(projectId, 10, 0, cancellationToken);

        var totalTasks = statusCounts.Sum(s => s.Count);
        var completed = statusCounts.Where(s => s.Status == ProjectTaskStatus.Done).Sum(s => s.Count);
        var pending = totalTasks - completed;

        var statuses = statusCounts
            .Select(s => new StatusCount(s.Status, s.Count))
            .ToList();

        var priorities = priorityCounts
            .Select(p => new PriorityCount(p.Priority, p.Count))
            .ToList();

        var activityItems = recentActivities
            .OrderByDescending(a => a.CreatedAt)
            .Take(10)
            .Select(a => new RecentActivity(a.Id, a.Type.ToString(), a.Message, a.User.DisplayName, a.CreatedAt))
            .ToList();

        return new ProjectDashboardResponse(
            projectId,
            totalTasks,
            completed,
            pending,
            criticalCount,
            statuses,
            priorities,
            memberCount,
            activityItems);
    }
}
