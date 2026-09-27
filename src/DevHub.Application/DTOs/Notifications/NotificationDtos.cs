using DevHub.Domain.Enums;

namespace DevHub.Application.DTOs.Notifications;

public sealed record NotificationResponse(
    Guid Id,
    NotificationType Type,
    string Title,
    string Message,
    Guid? ProjectId,
    string? Data,
    bool IsRead,
    DateTime? ReadAt,
    DateTime CreatedAt);

public sealed record UnreadCountResponse(int Count);

public sealed record NotificationsResponse(
    IReadOnlyList<NotificationResponse> Items,
    int Page,
    int PageSize,
    long TotalItems,
    int TotalPages,
    int UnreadCount);
