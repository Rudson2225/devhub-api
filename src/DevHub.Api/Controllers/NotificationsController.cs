using DevHub.Application.Abstractions;
using DevHub.Application.DTOs.Notifications;
using DevHub.Application.UseCases;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DevHub.Api.Controllers;

[ApiController]
[Route("api/notifications")]
[Authorize]
public sealed class NotificationsController : ApiControllerBase
{
    private readonly ICurrentUserService _currentUser;
    private readonly INotificationService _notifications;

    public NotificationsController(ICurrentUserService currentUser, INotificationService notifications)
    {
        _currentUser = currentUser;
        _notifications = notifications;
    }

    [HttpGet]
    [ProducesResponseType(typeof(NotificationsResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<NotificationsResponse>> List([FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken cancellationToken)
        => Ok(await _notifications.ListAsync(GetCurrentUserId(), page ?? 1, pageSize ?? 30, cancellationToken));

    [HttpGet("unread-count")]
    [ProducesResponseType(typeof(UnreadCountResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<UnreadCountResponse>> UnreadCount(CancellationToken cancellationToken)
        => Ok(new UnreadCountResponse(await _notifications.GetUnreadCountAsync(GetCurrentUserId(), cancellationToken)));

    [HttpPost("{id:guid}/read")]
    [ProducesResponseType(typeof(NotificationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<NotificationResponse>> MarkRead(Guid id, CancellationToken cancellationToken)
        => Ok(await _notifications.MarkReadAsync(GetCurrentUserId(), id, cancellationToken));

    [HttpPost("read-all")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> MarkAllRead(CancellationToken cancellationToken)
    {
        await _notifications.MarkAllReadAsync(GetCurrentUserId(), cancellationToken);
        return NoContent();
    }

    private Guid GetCurrentUserId()
        => _currentUser.UserId
           ?? throw new DevHub.Application.Common.Exceptions.UnauthorizedException("Authentication required.");
}
