using DevHub.Domain.Enums;

namespace DevHub.Application.DTOs.Tasks;

public sealed record CreateTaskRequest(
    string Title,
    string? Description,
    ProjectTaskStatus? Status,
    ProjectTaskPriority? Priority,
    Guid? AssigneeId,
    DateTime? DueDate,
    IReadOnlyList<Guid>? TagIds);

public sealed record UpdateTaskRequest(
    string? Title,
    string? Description,
    ProjectTaskStatus? Status,
    ProjectTaskPriority? Priority,
    Guid? AssigneeId,
    DateTime? DueDate,
    IReadOnlyList<Guid>? TagIds);

public sealed record TaskQueryParams(
    string? Search,
    ProjectTaskStatus? Status,
    ProjectTaskPriority? Priority,
    Guid? AssigneeId,
    Guid? TagId,
    string? SortBy,
    string? SortOrder,
    int? Page,
    int? PageSize);

public sealed record TaskResponse(
    Guid Id,
    string Title,
    string? Description,
    ProjectTaskStatus Status,
    ProjectTaskPriority Priority,
    Guid? AssigneeId,
    string? AssigneeName,
    Guid ReporterId,
    string ReporterName,
    DateTime? DueDate,
    DateTime? CompletedAt,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    int CommentCount,
    IReadOnlyList<TaskTagResponse> Tags);

public sealed record TaskTagResponse(Guid Id, string Name, string Color);

public sealed record TaskKanbanResponse(
    IReadOnlyList<TaskResponse> Backlog,
    IReadOnlyList<TaskResponse> Todo,
    IReadOnlyList<TaskResponse> InProgress,
    IReadOnlyList<TaskResponse> Review,
    IReadOnlyList<TaskResponse> Done);
