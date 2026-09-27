using DevHub.Domain.Enums;

namespace DevHub.Application.DTOs.Dashboard;

public sealed record ProjectDashboardResponse(
    Guid ProjectId,
    int TotalTasks,
    int CompletedTasks,
    int PendingTasks,
    int CriticalTasks,
    IReadOnlyList<StatusCount> TasksByStatus,
    IReadOnlyList<PriorityCount> TasksByPriority,
    int TotalMembers,
    IReadOnlyList<RecentActivity> RecentActivities);

public sealed record StatusCount(ProjectTaskStatus Status, int Count);

public sealed record PriorityCount(ProjectTaskPriority Priority, int Count);

public sealed record RecentActivity(
    Guid Id,
    string Type,
    string Message,
    string UserName,
    DateTime CreatedAt);
