using DevHub.Application.DTOs.Tags;
using DevHub.Domain.Entities;
using FluentValidation;

namespace DevHub.Application.Validation;

public sealed class CreateTagRequestValidator : AbstractValidator<CreateTagRequest>
{
    public CreateTagRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Tag name is required.")
            .MaximumLength(Tag.MaxNameLength)
            .Matches("^[a-zA-Z0-9 _-]+$").WithMessage("Tag name can only contain letters, numbers, spaces, underscores and hyphens.");

        RuleFor(x => x.Description)
            .MaximumLength(500);

        RuleFor(x => x.Color)
            .Matches("^#[0-9a-fA-F]{6}$").WithMessage("Color must be a valid hex color, e.g. #RRGGBB.")
            .When(x => x.Color is not null);
    }
}

public sealed class UpdateTagRequestValidator : AbstractValidator<UpdateTagRequest>
{
    public UpdateTagRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Tag name is required.")
            .MaximumLength(Tag.MaxNameLength)
            .Matches("^[a-zA-Z0-9 _-]+$").WithMessage("Tag name can only contain letters, numbers, spaces, underscores and hyphens.")
            .When(x => x.Name is not null);

        RuleFor(x => x.Description)
            .MaximumLength(500);

        RuleFor(x => x.Color)
            .Matches("^#[0-9a-fA-F]{6}$").WithMessage("Color must be a valid hex color, e.g. #RRGGBB.")
            .When(x => x.Color is not null);
    }
}
