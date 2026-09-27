using DevHub.Application.Abstractions;
using DevHub.Application.DTOs.Documents;
using DevHub.Application.UseCases;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DevHub.Api.Controllers;

[ApiController]
[Route("api")]
[Authorize]
public sealed class DocumentsController : ApiControllerBase
{
    private readonly ICurrentUserService _currentUser;
    private readonly IDocumentService _documents;

    public DocumentsController(ICurrentUserService currentUser, IDocumentService documents)
    {
        _currentUser = currentUser;
        _documents = documents;
    }

    [HttpGet("projects/{projectId:guid}/documents")]
    [ProducesResponseType(typeof(IReadOnlyList<DocumentSummaryResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<DocumentSummaryResponse>>> List(Guid projectId, CancellationToken cancellationToken)
        => Ok(await _documents.ListByProjectAsync(GetCurrentUserId(), projectId, cancellationToken));

    [HttpPost("projects/{projectId:guid}/documents")]
    [ProducesResponseType(typeof(DocumentResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<DocumentResponse>> Create(Guid projectId, [FromBody] CreateDocumentRequest request, CancellationToken cancellationToken)
    {
        await ValidateAsync(request, cancellationToken);
        var response = await _documents.CreateAsync(GetCurrentUserId(), projectId, request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = response.Id }, response);
    }

    [HttpGet("documents/{id:guid}")]
    [ProducesResponseType(typeof(DocumentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DocumentResponse>> Get(Guid id, CancellationToken cancellationToken)
        => Ok(await _documents.GetAsync(GetCurrentUserId(), id, cancellationToken));

    [HttpPatch("documents/{id:guid}")]
    [ProducesResponseType(typeof(DocumentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DocumentResponse>> Update(Guid id, [FromBody] UpdateDocumentRequest request, CancellationToken cancellationToken)
    {
        await ValidateAsync(request, cancellationToken);
        return Ok(await _documents.UpdateAsync(GetCurrentUserId(), id, request, cancellationToken));
    }

    [HttpDelete("documents/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await _documents.DeleteAsync(GetCurrentUserId(), id, cancellationToken);
        return NoContent();
    }

    private Guid GetCurrentUserId()
        => _currentUser.UserId
           ?? throw new DevHub.Application.Common.Exceptions.UnauthorizedException("Authentication required.");
}
