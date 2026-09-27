using DevHub.Application.Abstractions;
using DevHub.Application.DTOs.Comments;
using DevHub.Application.UseCases;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DevHub.Api.Controllers;

[ApiController]
[Route("api")]
[Authorize]
public sealed class TaskCommentsController : ApiControllerBase
{
    private readonly ICurrentUserService _currentUser;
    private readonly ITaskCommentService _comments;

    public TaskCommentsController(ICurrentUserService currentUser, ITaskCommentService comments)
    {
        _currentUser = currentUser;
        _comments = comments;
    }

    [HttpGet("tasks/{taskId:guid}/comments")]
    [ProducesResponseType(typeof(IReadOnlyList<CommentResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<CommentResponse>>> List(Guid taskId, CancellationToken cancellationToken)
        => Ok(await _comments.ListAsync(GetCurrentUserId(), taskId, cancellationToken));

    [HttpPost("tasks/{taskId:guid}/comments")]
    [ProducesResponseType(typeof(CommentResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<CommentResponse>> Create(Guid taskId, [FromBody] CreateCommentRequest request, CancellationToken cancellationToken)
    {
        await ValidateAsync(request, cancellationToken);
        var response = await _comments.CreateAsync(GetCurrentUserId(), taskId, request, cancellationToken);
        return CreatedAtAction(nameof(List), new { taskId }, response);
    }

    [HttpPatch("comments/{id:guid}")]
    [ProducesResponseType(typeof(CommentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<CommentResponse>> Update(Guid id, [FromBody] UpdateCommentRequest request, CancellationToken cancellationToken)
    {
        await ValidateAsync(request, cancellationToken);
        return Ok(await _comments.UpdateAsync(GetCurrentUserId(), id, request, cancellationToken));
    }

    [HttpDelete("comments/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await _comments.DeleteAsync(GetCurrentUserId(), id, cancellationToken);
        return NoContent();
    }

    private Guid GetCurrentUserId()
        => _currentUser.UserId
           ?? throw new DevHub.Application.Common.Exceptions.UnauthorizedException("Authentication required.");
}
