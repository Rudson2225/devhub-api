using DevHub.Domain.Enums;

namespace DevHub.Application.DTOs.Documents;

public sealed record CreateDocumentRequest(string Title, DocumentType? Type, string Content);

public sealed record UpdateDocumentRequest(string? Title, DocumentType? Type, string? Content);

public sealed record DocumentSummaryResponse(Guid Id, string Title, DocumentType Type, Guid AuthorId, string AuthorName, DateTime CreatedAt, DateTime UpdatedAt);

public sealed record DocumentResponse(
    Guid Id,
    Guid ProjectId,
    string Title,
    DocumentType Type,
    string Content,
    Guid AuthorId,
    string AuthorName,
    DateTime CreatedAt,
    DateTime UpdatedAt);
