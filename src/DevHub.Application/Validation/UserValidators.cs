using DevHub.Application.DTOs.Users;
using FluentValidation;

namespace DevHub.Application.Validation;

public sealed class UpdateProfileRequestValidator : AbstractValidator<UpdateProfileRequest>
{
    public UpdateProfileRequestValidator()
    {
        RuleFor(x => x.DisplayName)
            .NotEmpty().WithMessage("Display name is required.")
            .MaximumLength(150);

        RuleFor(x => x.Bio)
            .MaximumLength(1000);

        RuleFor(x => x.AvatarUrl)
            .MaximumLength(2048);
    }
}
