namespace DevHub.Application.DTOs.Tags;

public sealed record CreateTagRequest(string Name, string? Description, string? Color);

public sealed record UpdateTagRequest(string? Name, string? Description, string? Color);

public sealed record TagResponse(Guid Id, string Name, string? Description, string Color, DateTime CreatedAt);

public sealed record TaskTagAssignmentRequest(IReadOnlyList<Guid> TagIds);
