using DevHub.Application.Abstractions;
using DevHub.Application.Common.Exceptions;
using DevHub.Application.DTOs.Notifications;
using DevHub.Application.Mapping;

namespace DevHub.Application.UseCases;

public interface INotificationService
{
    Task<NotificationsResponse> ListAsync(Guid userId, int page, int pageSize, CancellationToken cancellationToken = default);

    Task<int> GetUnreadCountAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<NotificationResponse> MarkReadAsync(Guid userId, Guid notificationId, CancellationToken cancellationToken = default);

    Task MarkAllReadAsync(Guid userId, CancellationToken cancellationToken = default);
}

public sealed class NotificationService : INotificationService
{
    private readonly INotificationRepository _notifications;
    private readonly IUnitOfWork _unitOfWork;

    public NotificationService(INotificationRepository notifications, IUnitOfWork unitOfWork)
    {
        _notifications = notifications;
        _unitOfWork = unitOfWork;
    }

    public async Task<NotificationsResponse> ListAsync(Guid userId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var pagination = DevHub.Application.Common.PaginationQuery.Normalize(page, pageSize);

        var items = await _notifications.ListByUserAsync(userId, pagination.Page, pagination.PageSize, cancellationToken);
        var total = await _notifications.CountByUserAsync(userId, cancellationToken);
        var unread = await _notifications.CountUnreadByUserAsync(userId, cancellationToken);

        var totalPages = total == 0 ? 0 : (int)Math.Ceiling((double)total / pagination.PageSize);

        return new NotificationsResponse(
            items.Select(n => n.ToResponse()).ToList(),
            pagination.Page,
            pagination.PageSize,
            total,
            totalPages,
            unread);
    }

    public async Task<int> GetUnreadCountAsync(Guid userId, CancellationToken cancellationToken = default)
        => await _notifications.CountUnreadByUserAsync(userId, cancellationToken);

    public async Task<NotificationResponse> MarkReadAsync(Guid userId, Guid notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notifications.GetByIdAsync(notificationId, cancellationToken)
            ?? throw new NotFoundException("Notification not found.");

        if (notification.UserId != userId)
        {
            throw new ForbiddenException("You cannot access this notification.");
        }

        if (!notification.IsRead)
        {
            notification.IsRead = true;
            notification.ReadAt = DateTime.UtcNow;
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return notification.ToResponse();
    }

    public async Task MarkAllReadAsync(Guid userId, CancellationToken cancellationToken = default)
        => await _notifications.MarkAllReadAsync(userId, DateTime.UtcNow, cancellationToken);
}
