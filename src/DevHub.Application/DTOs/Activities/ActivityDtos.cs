using DevHub.Domain.Enums;

namespace DevHub.Application.DTOs.Activities;

public sealed record ActivityResponse(
    Guid Id,
    ActivityType Type,
    string Message,
    Guid UserId,
    string UserName,
    string? Data,
    DateTime CreatedAt);
