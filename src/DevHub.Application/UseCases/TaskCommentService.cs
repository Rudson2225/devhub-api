using DevHub.Application.Abstractions;
using DevHub.Application.Common.Exceptions;
using DevHub.Application.DTOs.Comments;
using DevHub.Application.Mapping;
using DevHub.Domain;
using DevHub.Domain.Entities;
using DevHub.Domain.Enums;

namespace DevHub.Application.UseCases;

public interface ITaskCommentService
{
    Task<IReadOnlyList<CommentResponse>> ListAsync(Guid actorId, Guid taskId, CancellationToken cancellationToken = default);

    Task<CommentResponse> CreateAsync(Guid actorId, Guid taskId, CreateCommentRequest request, CancellationToken cancellationToken = default);

    Task<CommentResponse> UpdateAsync(Guid actorId, Guid commentId, UpdateCommentRequest request, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid actorId, Guid commentId, CancellationToken cancellationToken = default);
}

public sealed class TaskCommentService : ITaskCommentService
{
    private readonly ITaskRepository _tasks;
    private readonly ITaskCommentRepository _comments;
    private readonly IActivityRepository _activities;
    private readonly INotificationRepository _notifications;
    private readonly IProjectAccessService _access;
    private readonly IUnitOfWork _unitOfWork;

    public TaskCommentService(
        ITaskRepository tasks,
        ITaskCommentRepository comments,
        IActivityRepository activities,
        INotificationRepository notifications,
        IProjectAccessService access,
        IUnitOfWork unitOfWork)
    {
        _tasks = tasks;
        _comments = comments;
        _activities = activities;
        _notifications = notifications;
        _access = access;
        _unitOfWork = unitOfWork;
    }

    public async Task<IReadOnlyList<CommentResponse>> ListAsync(Guid actorId, Guid taskId, CancellationToken cancellationToken = default)
    {
        var task = await GetAccessibleTaskAsync(actorId, taskId, Permissions.CommentsRead, cancellationToken);

        var comments = await _comments.ListByTaskAsync(task.Id, cancellationToken);
        return comments
            .OrderBy(c => c.CreatedAt)
            .Select(c => c.ToResponse())
            .ToList();
    }

    public async Task<CommentResponse> CreateAsync(Guid actorId, Guid taskId, CreateCommentRequest request, CancellationToken cancellationToken = default)
    {
        var task = await GetAccessibleTaskAsync(actorId, taskId, Permissions.CommentsCreate, cancellationToken);

        var comment = new TaskComment
        {
            TaskId = task.Id,
            ProjectId = task.ProjectId,
            AuthorId = actorId,
            Content = request.Content.Trim()
        };

        await _comments.AddAsync(comment, cancellationToken);

        await _activities.AddAsync(new ActivityLog
        {
            ProjectId = task.ProjectId,
            UserId = actorId,
            Type = ActivityType.CommentCreated,
            Message = $"A comment was added to task '{task.Title}'."
        }, cancellationToken);

        if (task.AssigneeId is not null && task.AssigneeId != actorId)
        {
            await _notifications.AddAsync(new Notification
            {
                UserId = task.AssigneeId.Value,
                ProjectId = task.ProjectId,
                Type = NotificationType.TaskComment,
                Title = "New comment on your task",
                Message = $"There is a new comment on task '{task.Title}'."
            }, cancellationToken);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var saved = await _comments.GetByIdAsync(comment.Id, cancellationToken)
            ?? throw new NotFoundException("Comment not found.");
        return saved.ToResponse();
    }

    public async Task<CommentResponse> UpdateAsync(Guid actorId, Guid commentId, UpdateCommentRequest request, CancellationToken cancellationToken = default)
    {
        var comment = await _comments.GetByIdAsync(commentId, cancellationToken)
            ?? throw new NotFoundException("Comment not found.");

        await EnsureCanModifyAsync(actorId, comment, allowOwn: Permissions.CommentsUpdateOwn, allowAny: Permissions.CommentsUpdateAny, cancellationToken);

        comment.Content = request.Content.Trim();

        await _activities.AddAsync(new ActivityLog
        {
            ProjectId = comment.ProjectId,
            UserId = actorId,
            Type = ActivityType.CommentUpdated,
            Message = "A comment was updated."
        }, cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var saved = await _comments.GetByIdAsync(comment.Id, cancellationToken)
            ?? throw new NotFoundException("Comment not found.");
        return saved.ToResponse();
    }

    public async Task DeleteAsync(Guid actorId, Guid commentId, CancellationToken cancellationToken = default)
    {
        var comment = await _comments.GetByIdAsync(commentId, cancellationToken)
            ?? throw new NotFoundException("Comment not found.");

        await EnsureCanModifyAsync(actorId, comment, allowOwn: Permissions.CommentsDeleteOwn, allowAny: Permissions.CommentsDeleteAny, cancellationToken);

        _comments.Remove(comment);

        await _activities.AddAsync(new ActivityLog
        {
            ProjectId = comment.ProjectId,
            UserId = actorId,
            Type = ActivityType.CommentDeleted,
            Message = "A comment was deleted."
        }, cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private async Task EnsureCanModifyAsync(Guid actorId, TaskComment comment, string allowOwn, string allowAny, CancellationToken cancellationToken)
    {
        if (comment.AuthorId == actorId)
        {
            await _access.EnsurePermissionAsync(comment.ProjectId, allowOwn, actorId, cancellationToken);
            return;
        }

        await _access.EnsurePermissionAsync(comment.ProjectId, allowAny, actorId, cancellationToken);
    }

    private async Task<ProjectTask> GetAccessibleTaskAsync(Guid actorId, Guid taskId, string permission, CancellationToken cancellationToken)
    {
        var task = await _tasks.GetByIdAsync(taskId, cancellationToken)
            ?? throw new NotFoundException("Task not found.");

        await _access.EnsurePermissionAsync(task.ProjectId, permission, actorId, cancellationToken);

        return task;
    }
}
