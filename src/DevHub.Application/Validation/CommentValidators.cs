using DevHub.Application.DTOs.Comments;
using DevHub.Domain.Entities;
using FluentValidation;

namespace DevHub.Application.Validation;

public sealed class CreateCommentRequestValidator : AbstractValidator<CreateCommentRequest>
{
    public CreateCommentRequestValidator()
    {
        RuleFor(x => x.Content)
            .NotEmpty().WithMessage("Comment content is required.")
            .MaximumLength(TaskComment.MaxContentLength);
    }
}

public sealed class UpdateCommentRequestValidator : AbstractValidator<UpdateCommentRequest>
{
    public UpdateCommentRequestValidator()
    {
        RuleFor(x => x.Content)
            .NotEmpty().WithMessage("Comment content is required.")
            .MaximumLength(TaskComment.MaxContentLength);
    }
}
