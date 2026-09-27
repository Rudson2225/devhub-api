namespace DevHub.Application.DTOs.Comments;

public sealed record CreateCommentRequest(string Content);

public sealed record UpdateCommentRequest(string Content);

public sealed record CommentResponse(
    Guid Id,
    Guid TaskId,
    Guid AuthorId,
    string AuthorName,
    string AuthorAvatarUrl,
    string Content,
    DateTime CreatedAt,
    DateTime UpdatedAt);
