using DevHub.Domain.Common;
using DevHub.Domain.Enums;

namespace DevHub.Domain.Entities;

public sealed class Document : BaseEntity
{
    public const int MaxTitleLength = 200;

    public Guid ProjectId { get; set; }

    public Project Project { get; set; } = null!;

    public Guid AuthorId { get; set; }

    public User Author { get; set; } = null!;

    public string Title { get; set; } = null!;

    public DocumentType Type { get; set; } = DocumentType.Other;

    public string Content { get; set; } = string.Empty;
}
