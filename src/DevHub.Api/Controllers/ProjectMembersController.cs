using DevHub.Application.Abstractions;
using DevHub.Application.DTOs.Projects;
using DevHub.Application.UseCases;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DevHub.Api.Controllers;

[ApiController]
[Route("api/projects/{projectId:guid}/members")]
[Authorize]
public sealed class ProjectMembersController : ApiControllerBase
{
    private readonly ICurrentUserService _currentUser;
    private readonly IProjectMemberService _members;

    public ProjectMembersController(ICurrentUserService currentUser, IProjectMemberService members)
    {
        _currentUser = currentUser;
        _members = members;
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<ProjectMemberResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<ProjectMemberResponse>>> List(Guid projectId, CancellationToken cancellationToken)
        => Ok(await _members.ListAsync(GetCurrentUserId(), projectId, cancellationToken));

    [HttpPost]
    [ProducesResponseType(typeof(ProjectMemberResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ProjectMemberResponse>> Add(Guid projectId, [FromBody] AddProjectMemberRequest request, CancellationToken cancellationToken)
    {
        await ValidateAsync(request, cancellationToken);
        var response = await _members.AddAsync(GetCurrentUserId(), projectId, request, cancellationToken);
        return CreatedAtAction(nameof(List), new { projectId }, response);
    }

    [HttpPatch("{userId:guid}")]
    [ProducesResponseType(typeof(ProjectMemberResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProjectMemberResponse>> UpdateRole(Guid projectId, Guid userId, [FromBody] UpdateMemberRoleRequest request, CancellationToken cancellationToken)
    {
        await ValidateAsync(request, cancellationToken);
        return Ok(await _members.UpdateRoleAsync(GetCurrentUserId(), projectId, userId, request, cancellationToken));
    }

    [HttpDelete("{userId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Remove(Guid projectId, Guid userId, CancellationToken cancellationToken)
    {
        await _members.RemoveAsync(GetCurrentUserId(), projectId, userId, cancellationToken);
        return NoContent();
    }

    private Guid GetCurrentUserId()
        => _currentUser.UserId
           ?? throw new DevHub.Application.Common.Exceptions.UnauthorizedException("Authentication required.");
}
