using DevHub.Application.Abstractions;
using DevHub.Application.DTOs.Tags;
using DevHub.Application.UseCases;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DevHub.Api.Controllers;

[ApiController]
[Route("api/projects/{projectId:guid}/tags")]
[Authorize]
public sealed class TagsController : ApiControllerBase
{
    private readonly ICurrentUserService _currentUser;
    private readonly ITagService _tags;

    public TagsController(ICurrentUserService currentUser, ITagService tags)
    {
        _currentUser = currentUser;
        _tags = tags;
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<TagResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<TagResponse>>> List(Guid projectId, CancellationToken cancellationToken)
        => Ok(await _tags.ListAsync(GetCurrentUserId(), projectId, cancellationToken));

    [HttpPost]
    [ProducesResponseType(typeof(TagResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<TagResponse>> Create(Guid projectId, [FromBody] CreateTagRequest request, CancellationToken cancellationToken)
    {
        await ValidateAsync(request, cancellationToken);
        var response = await _tags.CreateAsync(GetCurrentUserId(), projectId, request, cancellationToken);
        return CreatedAtAction(nameof(List), new { projectId }, response);
    }

    [HttpPatch("{tagId:guid}")]
    [ProducesResponseType(typeof(TagResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TagResponse>> Update(Guid projectId, Guid tagId, [FromBody] UpdateTagRequest request, CancellationToken cancellationToken)
    {
        await ValidateAsync(request, cancellationToken);
        return Ok(await _tags.UpdateAsync(GetCurrentUserId(), projectId, tagId, request, cancellationToken));
    }

    [HttpDelete("{tagId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid projectId, Guid tagId, CancellationToken cancellationToken)
    {
        await _tags.DeleteAsync(GetCurrentUserId(), projectId, tagId, cancellationToken);
        return NoContent();
    }

    private Guid GetCurrentUserId()
        => _currentUser.UserId
           ?? throw new DevHub.Application.Common.Exceptions.UnauthorizedException("Authentication required.");
}
