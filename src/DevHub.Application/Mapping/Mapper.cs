using DevHub.Application.DTOs.Activities;
using DevHub.Application.DTOs.Comments;
using DevHub.Application.DTOs.Documents;
using DevHub.Application.DTOs.Notifications;
using DevHub.Application.DTOs.Projects;
using DevHub.Application.DTOs.Tags;
using DevHub.Application.DTOs.Tasks;
using DevHub.Application.DTOs.Users;
using DevHub.Domain.Entities;

namespace DevHub.Application.Mapping;

public static class Mapper
{
    public static UserResponse ToResponse(this User user)
        => new(user.Id, user.Email, user.DisplayName, user.Bio, user.AvatarUrl, user.CreatedAt);

    public static ProjectSummaryResponse ToSummaryResponse(this Project project)
        => new(project.Id, project.Name, project.Description, project.Status, project.OwnerId, project.CreatedAt);

    public static ProjectResponse ToResponse(this Project project, int memberCount, int taskCount)
        => new(
            project.Id,
            project.Name,
            project.Description,
            project.Status,
            project.OwnerId,
            project.Owner.DisplayName,
            project.CreatedAt,
            project.UpdatedAt,
            memberCount,
            taskCount);

    public static ProjectMemberResponse ToResponse(this ProjectMember member)
        => new(
            member.UserId,
            member.User.Email,
            member.User.DisplayName,
            member.User.AvatarUrl,
            member.Role.Name,
            member.JoinedAt);

    public static RoleResponse ToResponse(this Role role)
        => new(role.Id, role.Name, role.Description, role.Permissions.Select(rp => rp.Permission.ToResponse()).ToList());

    public static PermissionResponse ToResponse(this Permission permission)
        => new(permission.Id, permission.Name, permission.Description);

    public static TaskResponse ToResponse(this ProjectTask task, int commentCount)
        => new(
            task.Id,
            task.Title,
            task.Description,
            task.Status,
            task.Priority,
            task.AssigneeId,
            task.Assignee?.DisplayName,
            task.ReporterId,
            task.Reporter.DisplayName,
            task.DueDate,
            task.CompletedAt,
            task.CreatedAt,
            task.UpdatedAt,
            commentCount,
            task.Tags.Select(t => t.ToResponse()).ToList());

    public static TaskTagResponse ToResponse(this TaskTag tag)
        => new(tag.Tag.Id, tag.Tag.Name, tag.Tag.Color);

    public static TagResponse ToResponse(this Tag tag)
        => new(tag.Id, tag.Name, tag.Description, tag.Color, tag.CreatedAt);

    public static CommentResponse ToResponse(this TaskComment comment)
        => new(
            comment.Id,
            comment.TaskId,
            comment.AuthorId,
            comment.Author.DisplayName,
            comment.Author.AvatarUrl ?? string.Empty,
            comment.Content,
            comment.CreatedAt,
            comment.UpdatedAt);

    public static DocumentSummaryResponse ToSummaryResponse(this Document document)
        => new(document.Id, document.Title, document.Type, document.AuthorId, document.Author.DisplayName, document.CreatedAt, document.UpdatedAt);

    public static DocumentResponse ToResponse(this Document document)
        => new(
            document.Id,
            document.ProjectId,
            document.Title,
            document.Type,
            document.Content,
            document.AuthorId,
            document.Author.DisplayName,
            document.CreatedAt,
            document.UpdatedAt);

    public static ActivityResponse ToResponse(this ActivityLog activity)
        => new(activity.Id, activity.Type, activity.Message, activity.UserId, activity.User.DisplayName, activity.Data, activity.CreatedAt);

    public static NotificationResponse ToResponse(this Notification notification)
        => new(
            notification.Id,
            notification.Type,
            notification.Title,
            notification.Message,
            notification.ProjectId,
            notification.Data,
            notification.IsRead,
            notification.ReadAt,
            notification.CreatedAt);
}
