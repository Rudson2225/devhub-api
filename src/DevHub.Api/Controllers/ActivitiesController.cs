using DevHub.Application.Abstractions;
using DevHub.Application.Common;
using DevHub.Application.DTOs.Activities;
using DevHub.Application.UseCases;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DevHub.Api.Controllers;

[ApiController]
[Route("api/projects/{projectId:guid}/activities")]
[Authorize]
public sealed class ActivitiesController : ApiControllerBase
{
    private readonly ICurrentUserService _currentUser;
    private readonly IActivityService _activities;

    public ActivitiesController(ICurrentUserService currentUser, IActivityService activities)
    {
        _currentUser = currentUser;
        _activities = activities;
    }

    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<ActivityResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PagedResult<ActivityResponse>>> List(
        Guid projectId,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken cancellationToken)
        => Ok(await _activities.ListAsync(GetCurrentUserId(), projectId, page ?? 1, pageSize ?? 30, cancellationToken));

    private Guid GetCurrentUserId()
        => _currentUser.UserId
           ?? throw new DevHub.Application.Common.Exceptions.UnauthorizedException("Authentication required.");
}
