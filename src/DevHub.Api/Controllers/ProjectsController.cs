using DevHub.Application.Abstractions;
using DevHub.Application.Common;
using DevHub.Application.DTOs.Projects;
using DevHub.Application.UseCases;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DevHub.Api.Controllers;

[ApiController]
[Route("api/projects")]
[Authorize]
public sealed class ProjectsController : ApiControllerBase
{
    private readonly ICurrentUserService _currentUser;
    private readonly IProjectService _projects;

    public ProjectsController(ICurrentUserService currentUser, IProjectService projects)
    {
        _currentUser = currentUser;
        _projects = projects;
    }

    [HttpPost]
    [ProducesResponseType(typeof(ProjectResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ProjectResponse>> Create([FromBody] CreateProjectRequest request, CancellationToken cancellationToken)
    {
        await ValidateAsync(request, cancellationToken);
        var response = await _projects.CreateAsync(GetCurrentUserId(), request, cancellationToken);

        // Point at the Get route, not at this one: CreatedAtAction(nameof(Create)) produced
        // "api/projects?id=...", which is the collection, not the project that was just created.
        return CreatedAtAction(nameof(Get), new { projectId = response.Id }, response);
    }

    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<ProjectSummaryResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<ProjectSummaryResponse>>> List(
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken cancellationToken)
        => Ok(await _projects.ListAsync(GetCurrentUserId(), page ?? 1, pageSize ?? 20, cancellationToken));

    [HttpGet("{projectId:guid}")]
    [ProducesResponseType(typeof(ProjectResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProjectResponse>> Get(Guid projectId, CancellationToken cancellationToken)
        => Ok(await _projects.GetAsync(GetCurrentUserId(), projectId, cancellationToken));

    [HttpPatch("{projectId:guid}")]
    [ProducesResponseType(typeof(ProjectResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProjectResponse>> Update(Guid projectId, [FromBody] UpdateProjectRequest request, CancellationToken cancellationToken)
    {
        await ValidateAsync(request, cancellationToken);
        return Ok(await _projects.UpdateAsync(GetCurrentUserId(), projectId, request, cancellationToken));
    }

    [HttpDelete("{projectId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid projectId, CancellationToken cancellationToken)
    {
        await _projects.DeleteAsync(GetCurrentUserId(), projectId, cancellationToken);
        return NoContent();
    }

    private Guid GetCurrentUserId()
        => _currentUser.UserId
           ?? throw new DevHub.Application.Common.Exceptions.UnauthorizedException("Authentication required.");
}
