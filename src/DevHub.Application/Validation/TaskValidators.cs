using DevHub.Application.DTOs.Tasks;
using DevHub.Domain.Entities;
using FluentValidation;

namespace DevHub.Application.Validation;

public sealed class CreateTaskRequestValidator : AbstractValidator<CreateTaskRequest>
{
    public CreateTaskRequestValidator()
    {
        RuleFor(x => x.Title)
            .NotEmpty().WithMessage("Task title is required.")
            .MaximumLength(ProjectTask.MaxTitleLength);

        RuleFor(x => x.Description)
            .MaximumLength(ProjectTask.MaxDescriptionLength);

        RuleFor(x => x.Status)
            .IsInEnum().WithMessage("Invalid task status.")
            .When(x => x.Status is not null);

        RuleFor(x => x.Priority)
            .IsInEnum().WithMessage("Invalid task priority.")
            .When(x => x.Priority is not null);
    }
}

public sealed class UpdateTaskRequestValidator : AbstractValidator<UpdateTaskRequest>
{
    public UpdateTaskRequestValidator()
    {
        RuleFor(x => x.Title)
            .NotEmpty().WithMessage("Task title is required.")
            .MaximumLength(ProjectTask.MaxTitleLength)
            .When(x => x.Title is not null);

        RuleFor(x => x.Description)
            .MaximumLength(ProjectTask.MaxDescriptionLength);

        RuleFor(x => x.Status)
            .IsInEnum().WithMessage("Invalid task status.")
            .When(x => x.Status is not null);

        RuleFor(x => x.Priority)
            .IsInEnum().WithMessage("Invalid task priority.")
            .When(x => x.Priority is not null);
    }
}
