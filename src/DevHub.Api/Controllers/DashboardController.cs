using DevHub.Application.Abstractions;
using DevHub.Application.DTOs.Dashboard;
using DevHub.Application.UseCases;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DevHub.Api.Controllers;

[ApiController]
[Route("api")]
[Authorize]
public sealed class DashboardController : ApiControllerBase
{
    private readonly ICurrentUserService _currentUser;
    private readonly IDashboardService _dashboard;

    public DashboardController(ICurrentUserService currentUser, IDashboardService dashboard)
    {
        _currentUser = currentUser;
        _dashboard = dashboard;
    }

    [HttpGet("projects/{projectId:guid}/dashboard")]
    [ProducesResponseType(typeof(ProjectDashboardResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProjectDashboardResponse>> ProjectDashboard(Guid projectId, CancellationToken cancellationToken)
        => Ok(await _dashboard.GetProjectDashboardAsync(GetCurrentUserId(), projectId, cancellationToken));

    private Guid GetCurrentUserId()
        => _currentUser.UserId
           ?? throw new DevHub.Application.Common.Exceptions.UnauthorizedException("Authentication required.");
}
