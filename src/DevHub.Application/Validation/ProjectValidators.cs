using DevHub.Application.DTOs.Projects;
using DevHub.Domain.Entities;
using DevHub.Domain.Enums;
using FluentValidation;

namespace DevHub.Application.Validation;

public sealed class CreateProjectRequestValidator : AbstractValidator<CreateProjectRequest>
{
    public CreateProjectRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Project name is required.")
            .MaximumLength(Project.MaxNameLength);

        RuleFor(x => x.Description)
            .MaximumLength(Project.MaxDescriptionLength);

        RuleFor(x => x.Status)
            .IsInEnum().WithMessage("Invalid project status.")
            .NotEqual(ProjectStatus.Archived).WithMessage("A new project cannot be created as archived.");
    }
}

public sealed class UpdateProjectRequestValidator : AbstractValidator<UpdateProjectRequest>
{
    public UpdateProjectRequestValidator()
    {
        RuleFor(x => x.Name)
            .MaximumLength(Project.MaxNameLength)
            .When(x => x.Name is not null);

        RuleFor(x => x.Description)
            .MaximumLength(Project.MaxDescriptionLength);

        RuleFor(x => x.Status)
            .IsInEnum().WithMessage("Invalid project status.")
            .When(x => x.Status is not null);
    }
}

public sealed class AddProjectMemberRequestValidator : AbstractValidator<AddProjectMemberRequest>
{
    private static readonly string[] ValidRoles =
    [
        Role.Admin,
        Role.Member,
        Role.Viewer
    ];

    public AddProjectMemberRequestValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required.")
            .EmailAddress().WithMessage("A valid email address is required.");

        RuleFor(x => x.Role)
            .NotEmpty().WithMessage("Role is required.")
            .Must(r => ValidRoles.Contains(r)).WithMessage($"Role must be one of: {string.Join(", ", ValidRoles)}.");
    }
}

public sealed class UpdateMemberRoleRequestValidator : AbstractValidator<UpdateMemberRoleRequest>
{
    private static readonly string[] ValidRoles =
    [
        Role.Admin,
        Role.Member,
        Role.Viewer
    ];

    public UpdateMemberRoleRequestValidator()
    {
        RuleFor(x => x.Role)
            .NotEmpty().WithMessage("Role is required.")
            .Must(r => ValidRoles.Contains(r)).WithMessage($"Role must be one of: {string.Join(", ", ValidRoles)}.");
    }
}
