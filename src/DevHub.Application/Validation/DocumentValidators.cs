using DevHub.Application.DTOs.Documents;
using DevHub.Domain.Entities;
using FluentValidation;

namespace DevHub.Application.Validation;

public sealed class CreateDocumentRequestValidator : AbstractValidator<CreateDocumentRequest>
{
    public CreateDocumentRequestValidator()
    {
        RuleFor(x => x.Title)
            .NotEmpty().WithMessage("Document title is required.")
            .MaximumLength(Document.MaxTitleLength);

        RuleFor(x => x.Type)
            .IsInEnum().WithMessage("Invalid document type.")
            .When(x => x.Type is not null);

        RuleFor(x => x.Content)
            .NotNull().WithMessage("Document content cannot be null.")
            .MaximumLength(100_000);
    }
}

public sealed class UpdateDocumentRequestValidator : AbstractValidator<UpdateDocumentRequest>
{
    public UpdateDocumentRequestValidator()
    {
        RuleFor(x => x.Title)
            .NotEmpty().WithMessage("Document title is required.")
            .MaximumLength(Document.MaxTitleLength)
            .When(x => x.Title is not null);

        RuleFor(x => x.Type)
            .IsInEnum().WithMessage("Invalid document type.")
            .When(x => x.Type is not null);

        RuleFor(x => x.Content)
            .MaximumLength(100_000)
            .When(x => x.Content is not null);
    }
}
