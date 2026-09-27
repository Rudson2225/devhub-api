using DevHub.Application.Abstractions;
using DevHub.Application.Common;
using DevHub.Application.DTOs.Tasks;
using DevHub.Application.UseCases;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DevHub.Api.Controllers;

[ApiController]
[Route("api")]
[Authorize]
public sealed class TasksController : ApiControllerBase
{
    private readonly ICurrentUserService _currentUser;
    private readonly ITaskService _tasks;

    public TasksController(ICurrentUserService currentUser, ITaskService tasks)
    {
        _currentUser = currentUser;
        _tasks = tasks;
    }

    [HttpPost("projects/{projectId:guid}/tasks")]
    [ProducesResponseType(typeof(TaskResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<TaskResponse>> Create(Guid projectId, [FromBody] CreateTaskRequest request, CancellationToken cancellationToken)
    {
        await ValidateAsync(request, cancellationToken);
        var response = await _tasks.CreateAsync(GetCurrentUserId(), projectId, request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = response.Id }, response);
    }

    [HttpGet("projects/{projectId:guid}/tasks")]
    [ProducesResponseType(typeof(PagedResult<TaskResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PagedResult<TaskResponse>>> List(Guid projectId, [FromQuery] TaskQueryParams query, CancellationToken cancellationToken)
        => Ok(await _tasks.ListAsync(GetCurrentUserId(), projectId, query, cancellationToken));

    [HttpGet("projects/{projectId:guid}/tasks/board")]
    [ProducesResponseType(typeof(TaskKanbanResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<TaskKanbanResponse>> Board(Guid projectId, CancellationToken cancellationToken)
        => Ok(await _tasks.GetKanbanAsync(GetCurrentUserId(), projectId, cancellationToken));

    [HttpGet("tasks/{id:guid}")]
    [ProducesResponseType(typeof(TaskResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TaskResponse>> Get(Guid id, CancellationToken cancellationToken)
        => Ok(await _tasks.GetAsync(GetCurrentUserId(), id, cancellationToken));

    [HttpPatch("tasks/{id:guid}")]
    [ProducesResponseType(typeof(TaskResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<TaskResponse>> Update(Guid id, [FromBody] UpdateTaskRequest request, CancellationToken cancellationToken)
    {
        await ValidateAsync(request, cancellationToken);
        return Ok(await _tasks.UpdateAsync(GetCurrentUserId(), id, request, cancellationToken));
    }

    [HttpDelete("tasks/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await _tasks.DeleteAsync(GetCurrentUserId(), id, cancellationToken);
        return NoContent();
    }

    private Guid GetCurrentUserId()
        => _currentUser.UserId
           ?? throw new DevHub.Application.Common.Exceptions.UnauthorizedException("Authentication required.");
}
