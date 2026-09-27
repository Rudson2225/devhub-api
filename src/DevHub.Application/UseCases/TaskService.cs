using DevHub.Application.Abstractions;
using DevHub.Application.Common;
using DevHub.Application.Common.Exceptions;
using DevHub.Application.DTOs.Tasks;
using DevHub.Application.Mapping;
using DevHub.Domain;
using DevHub.Domain.Entities;
using DevHub.Domain.Enums;

namespace DevHub.Application.UseCases;

public interface ITaskService
{
    Task<PagedResult<TaskResponse>> ListAsync(Guid actorId, Guid projectId, TaskQueryParams query, CancellationToken cancellationToken = default);

    Task<TaskKanbanResponse> GetKanbanAsync(Guid actorId, Guid projectId, CancellationToken cancellationToken = default);

    Task<TaskResponse> GetAsync(Guid actorId, Guid taskId, CancellationToken cancellationToken = default);

    Task<TaskResponse> CreateAsync(Guid actorId, Guid projectId, CreateTaskRequest request, CancellationToken cancellationToken = default);

    Task<TaskResponse> UpdateAsync(Guid actorId, Guid taskId, UpdateTaskRequest request, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid actorId, Guid taskId, CancellationToken cancellationToken = default);
}

public sealed class TaskService : ITaskService
{
    private static readonly (string, ProjectTaskStatus)[] StatusColumns =
    [
        ("backlog", ProjectTaskStatus.Backlog),
        ("todo", ProjectTaskStatus.Todo),
        ("inprogress", ProjectTaskStatus.InProgress),
        ("review", ProjectTaskStatus.Review),
        ("done", ProjectTaskStatus.Done)
    ];

    private readonly ITaskRepository _tasks;
    private readonly ITaskCommentRepository _comments;
    private readonly ITagRepository _tags;
    private readonly IProjectMemberRepository _members;
    private readonly IActivityRepository _activities;
    private readonly INotificationRepository _notifications;
    private readonly IProjectAccessService _access;
    private readonly IUnitOfWork _unitOfWork;

    public TaskService(
        ITaskRepository tasks,
        ITaskCommentRepository comments,
        ITagRepository tags,
        IProjectMemberRepository members,
        IActivityRepository activities,
        INotificationRepository notifications,
        IProjectAccessService access,
        IUnitOfWork unitOfWork)
    {
        _tasks = tasks;
        _comments = comments;
        _tags = tags;
        _members = members;
        _activities = activities;
        _notifications = notifications;
        _access = access;
        _unitOfWork = unitOfWork;
    }

    public async Task<PagedResult<TaskResponse>> ListAsync(Guid actorId, Guid projectId, TaskQueryParams query, CancellationToken cancellationToken = default)
    {
        await _access.EnsurePermissionAsync(projectId, Permissions.TasksRead, actorId, cancellationToken);

        var pagination = PaginationQuery.Normalize(query.Page, query.PageSize);
        var listQuery = new TaskListQuery(
            query.Search,
            query.Status,
            query.Priority,
            query.AssigneeId,
            query.TagId,
            query.SortBy,
            query.SortOrder,
            pagination.Page,
            pagination.PageSize);

        var tasks = await _tasks.ListAsync(projectId, listQuery, cancellationToken);
        var total = await _tasks.CountAsync(projectId, listQuery, cancellationToken);

        var responses = await MapTaskResponsesAsync(tasks, cancellationToken);

        return PagedResult<TaskResponse>.Create(responses, pagination.Page, pagination.PageSize, total);
    }

    public async Task<TaskKanbanResponse> GetKanbanAsync(Guid actorId, Guid projectId, CancellationToken cancellationToken = default)
    {
        await _access.EnsurePermissionAsync(projectId, Permissions.TasksRead, actorId, cancellationToken);

        var groups = new Dictionary<ProjectTaskStatus, List<TaskResponse>>();
        foreach (var (_, status) in StatusColumns)
        {
            // One capped query per column rather than a single unbounded read of the project. The
            // board therefore shows at most MaxPageSize tasks per column and the response carries no
            // total, so a column with more tasks than that is silently truncated: the list endpoint
            // is the way to page past it.
            var tasks = await _tasks.ListAsync(projectId, new TaskListQuery(null, status, null, null, null, null, null, 1, PaginationQuery.MaxPageSize), cancellationToken);
            groups[status] = await MapTaskResponsesAsync(tasks, cancellationToken);
        }

        return new TaskKanbanResponse(
            groups[ProjectTaskStatus.Backlog],
            groups[ProjectTaskStatus.Todo],
            groups[ProjectTaskStatus.InProgress],
            groups[ProjectTaskStatus.Review],
            groups[ProjectTaskStatus.Done]);
    }

    public async Task<TaskResponse> GetAsync(Guid actorId, Guid taskId, CancellationToken cancellationToken = default)
    {
        var task = await GetAccessibleTaskAsync(actorId, taskId, Permissions.TasksRead, cancellationToken);

        var commentCount = await _comments.CountByTaskIdsAsync([task.Id], cancellationToken);
        return task.ToResponse(commentCount.GetValueOrDefault(task.Id));
    }

    public async Task<TaskResponse> CreateAsync(Guid actorId, Guid projectId, CreateTaskRequest request, CancellationToken cancellationToken = default)
    {
        await _access.EnsurePermissionAsync(projectId, Permissions.TasksCreate, actorId, cancellationToken);

        ProjectMember? assigneeMember = null;
        if (request.AssigneeId is not null)
        {
            assigneeMember = await _members.GetMembershipAsync(projectId, request.AssigneeId.Value, cancellationToken);
            if (assigneeMember is null)
            {
                throw new BadRequestException("The assignee must be a member of the project.");
            }
        }

        var task = new ProjectTask
        {
            ProjectId = projectId,
            Title = request.Title.Trim(),
            Description = request.Description,
            Status = request.Status ?? ProjectTaskStatus.Backlog,
            Priority = request.Priority ?? ProjectTaskPriority.Medium,
            AssigneeId = request.AssigneeId,
            ReporterId = actorId,
            DueDate = request.DueDate
        };

        await _tasks.AddAsync(task, cancellationToken);
        await ApplyTagsAsync(task, request.TagIds, cancellationToken);

        var assignedText = assigneeMember is not null && assigneeMember.UserId != actorId
            ? $" and assigned to {assigneeMember.User.DisplayName}"
            : string.Empty;

        await _activities.AddAsync(new ActivityLog
        {
            ProjectId = projectId,
            UserId = actorId,
            Type = ActivityType.TaskCreated,
            Message = $"Task '{task.Title}' was created{assignedText}."
        }, cancellationToken);

        if (request.AssigneeId is not null && request.AssigneeId.Value != actorId)
        {
            await _notifications.AddAsync(new Notification
            {
                UserId = request.AssigneeId.Value,
                ProjectId = projectId,
                Type = NotificationType.TaskAssigned,
                Title = "New task assigned to you",
                Message = $"You were assigned to task '{task.Title}'."
            }, cancellationToken);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var saved = await _tasks.GetByIdAsync(task.Id, cancellationToken)
            ?? throw new NotFoundException("Task not found.");
        var count = await _comments.CountByTaskIdsAsync([task.Id], cancellationToken);

        return saved.ToResponse(count.GetValueOrDefault(task.Id));
    }

    public async Task<TaskResponse> UpdateAsync(Guid actorId, Guid taskId, UpdateTaskRequest request, CancellationToken cancellationToken = default)
    {
        var task = await GetAccessibleTaskAsync(actorId, taskId, Permissions.TasksUpdate, cancellationToken);

        ProjectMember? newAssignee = null;
        if (request.AssigneeId is not null && request.AssigneeId != task.AssigneeId)
        {
            newAssignee = await _members.GetMembershipAsync(task.ProjectId, request.AssigneeId.Value, cancellationToken);
            if (newAssignee is null)
            {
                throw new BadRequestException("The assignee must be a member of the project.");
            }
        }

        var wasCompleted = task.Status == ProjectTaskStatus.Done;
        var assigneeChanged = newAssignee is not null;

        if (request.Title is not null)
        {
            task.Title = request.Title.Trim();
        }

        if (request.Description is not null)
        {
            task.Description = request.Description;
        }

        if (request.Priority is not null)
        {
            task.Priority = request.Priority.Value;
        }

        if (request.DueDate is not null)
        {
            task.DueDate = request.DueDate;
        }

        var statusChanged = request.Status is not null && request.Status.Value != task.Status;
        if (statusChanged)
        {
            task.Status = request.Status!.Value;
            task.CompletedAt = task.Status == ProjectTaskStatus.Done ? DateTime.UtcNow : null;
        }

        if (assigneeChanged)
        {
            task.AssigneeId = request.AssigneeId;

            if (newAssignee!.UserId != actorId)
            {
                await _notifications.AddAsync(new Notification
                {
                    UserId = newAssignee.UserId,
                    ProjectId = task.ProjectId,
                    Type = NotificationType.TaskAssigned,
                    Title = "Task assigned to you",
                    Message = $"You were assigned to task '{task.Title}'."
                }, cancellationToken);
            }
        }

        await ApplyTagsAsync(task, request.TagIds, cancellationToken);
        await SaveTaskChangesLogAsync(task, actorId, wasCompleted, statusChanged, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var saved = await _tasks.GetByIdAsync(task.Id, cancellationToken)
            ?? throw new NotFoundException("Task not found.");
        var count = await _comments.CountByTaskIdsAsync([task.Id], cancellationToken);

        return saved.ToResponse(count.GetValueOrDefault(task.Id));
    }

    public async Task DeleteAsync(Guid actorId, Guid taskId, CancellationToken cancellationToken = default)
    {
        var task = await GetAccessibleTaskAsync(actorId, taskId, Permissions.TasksDelete, cancellationToken);
        var title = task.Title;
        var projectId = task.ProjectId;

        _tasks.Remove(task);

        await _activities.AddAsync(new ActivityLog
        {
            ProjectId = projectId,
            UserId = actorId,
            Type = ActivityType.TaskDeleted,
            Message = $"Task '{title}' was deleted."
        }, cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private async Task SaveTaskChangesLogAsync(
        ProjectTask task,
        Guid actorId,
        bool wasCompleted,
        bool statusChanged,
        CancellationToken cancellationToken)
    {
        if (task.Status == ProjectTaskStatus.Done && !wasCompleted)
        {
            await _activities.AddAsync(new ActivityLog
            {
                ProjectId = task.ProjectId,
                UserId = actorId,
                Type = ActivityType.TaskCompleted,
                Message = $"Task '{task.Title}' was completed."
            }, cancellationToken);
        }
        else if (statusChanged)
        {
            await _activities.AddAsync(new ActivityLog
            {
                ProjectId = task.ProjectId,
                UserId = actorId,
                Type = ActivityType.TaskStatusChanged,
                Message = $"Task '{task.Title}' moved to {task.Status}."
            }, cancellationToken);

            if (task.AssigneeId is not null && task.AssigneeId != actorId)
            {
                await _notifications.AddAsync(new Notification
                {
                    UserId = task.AssigneeId.Value,
                    ProjectId = task.ProjectId,
                    Type = NotificationType.TaskStatusChanged,
                    Title = "Task status changed",
                    Message = $"Task '{task.Title}' moved to {task.Status}."
                }, cancellationToken);
            }
        }
        else
        {
            await _activities.AddAsync(new ActivityLog
            {
                ProjectId = task.ProjectId,
                UserId = actorId,
                Type = ActivityType.TaskUpdated,
                Message = $"Task '{task.Title}' was updated."
            }, cancellationToken);
        }
    }

    /// <summary>
    /// Rewrites the task's tag associations to exactly <paramref name="tagIds"/>.
    /// <para>
    /// A null list means the caller did not mention tags at all, so the current associations are left
    /// untouched; an empty list means "remove every tag". The two are different requests, which is why
    /// an update that only moves a task to another column must never silently drop its labels.
    /// </para>
    /// <para>
    /// The change is applied as a diff rather than by deleting every row and inserting the desired set
    /// again. Deleting through a bulk statement would bypass the change tracker, leaving the loaded
    /// task still reporting tags the database no longer has, and re-inserting a tag that was just
    /// removed would collide with the tracked association for the same composite key.
    /// </para>
    /// </summary>
    private async Task ApplyTagsAsync(ProjectTask task, IReadOnlyList<Guid>? tagIds, CancellationToken cancellationToken)
    {
        if (tagIds is null)
        {
            return;
        }

        var desired = tagIds.Distinct().ToArray();

        // Every id is resolved before a single association is touched, so an unknown tag rejects the
        // whole request instead of leaving the task with half of the new set applied.
        var tags = new List<Tag>(desired.Length);
        foreach (var tagId in desired)
        {
            var tag = await _tags.GetByIdAsync(tagId, cancellationToken)
                ?? throw new NotFoundException($"Tag '{tagId}' not found.");

            // A tag owned by another project is not a label this task may carry. The permission that
            // got the caller here is scoped to task.ProjectId, so accepting a foreign tag would let a
            // member of one project pull a label out of another one - and the linking step below
            // would even attach it to this project. A global tag has no ProjectId and is usable
            // everywhere, so only a scoped tag from another project is refused here.
            if (tag.ProjectId is not null && tag.ProjectId != task.ProjectId)
            {
                throw new NotFoundException($"Tag '{tagId}' not found.");
            }

            tags.Add(tag);
        }

        var current = task.Tags.ToList();
        var desiredIds = desired.ToHashSet();

        foreach (var detached in current.Where(taskTag => !desiredIds.Contains(taskTag.TagId)))
        {
            _tags.RemoveTaskTag(detached);
        }

        var currentIds = current.Select(taskTag => taskTag.TagId).ToHashSet();
        foreach (var tag in tags.Where(candidate => !currentIds.Contains(candidate.Id)))
        {
            var isAssociated = await _tags.IsAssociatedWithProjectAsync(task.ProjectId, tag.Id, cancellationToken);
            if (!isAssociated)
            {
                await _tags.AddProjectTagAsync(new ProjectTag
                {
                    ProjectId = task.ProjectId,
                    TagId = tag.Id,
                    AddedAt = DateTime.UtcNow
                }, cancellationToken);
            }

            await _tags.AddTaskTagAsync(new TaskTag
            {
                TaskId = task.Id,
                TagId = tag.Id
            }, cancellationToken);
        }
    }

    private async Task<ProjectTask> GetAccessibleTaskAsync(Guid actorId, Guid taskId, string permission, CancellationToken cancellationToken)
    {
        var task = await _tasks.GetByIdAsync(taskId, cancellationToken)
            ?? throw new NotFoundException("Task not found.");

        await _access.EnsurePermissionAsync(task.ProjectId, permission, actorId, cancellationToken);

        return task;
    }

    private async Task<List<TaskResponse>> MapTaskResponsesAsync(IReadOnlyList<ProjectTask> tasks, CancellationToken cancellationToken)
    {
        var commentCounts = await _comments.CountByTaskIdsAsync(tasks.Select(t => t.Id).ToList(), cancellationToken);
        return tasks.Select(t => t.ToResponse(commentCounts.GetValueOrDefault(t.Id))).ToList();
    }
}
