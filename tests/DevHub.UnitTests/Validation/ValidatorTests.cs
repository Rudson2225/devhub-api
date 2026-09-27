using DevHub.Application.DTOs.Auth;
using DevHub.Application.DTOs.Projects;
using DevHub.Application.Validation;
using DevHub.Domain.Entities;
using DevHub.Domain.Enums;
using FluentAssertions;
using FluentValidation.TestHelper;

namespace DevHub.UnitTests.Validation;

public sealed class RegisterRequestValidatorTests
{
    private readonly RegisterRequestValidator _validator = new();

    [Theory]
    [InlineData("dev@example.com", "Str0ng!Pass123", "Dev")]
    [InlineData("a@b.co", "aA1aaaaa", "x")]
    public void Valid_register_request_passes(string email, string password, string displayName)
    {
        var result = _validator.TestValidate(new RegisterRequest(email, password, displayName));
        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("", "Str0ng!Pass123", "Dev")]
    [InlineData("not-an-email", "Str0ng!Pass123", "Dev")]
    [InlineData("dev@example.com", "weak", "Dev")]
    [InlineData("dev@example.com", "lowercase123", "Dev")]
    [InlineData("dev@example.com", "UPPERCASE123", "Dev")]
    [InlineData("dev@example.com", "NoDigitsAtAll!", "Dev")]
    [InlineData("dev@example.com", "Str0ng!Pass123", "")]
    public void Invalid_register_request_fails(string email, string password, string displayName)
    {
        var result = _validator.TestValidate(new RegisterRequest(email, password, displayName));
        result.IsValid.Should().BeFalse();
    }
}

public sealed class LoginRequestValidatorTests
{
    private readonly LoginRequestValidator _validator = new();

    [Fact]
    public void Valid_login_passes()
        => _validator.TestValidate(new LoginRequest("dev@example.com", "password")).IsValid.Should().BeTrue();

    [Fact]
    public void Missing_password_fails()
        => _validator.TestValidate(new LoginRequest("dev@example.com", "")).IsValid.Should().BeFalse();
}

public sealed class ChangePasswordRequestValidatorTests
{
    private readonly ChangePasswordRequestValidator _validator = new();

    [Fact]
    public void Valid_change_passes()
        => _validator.TestValidate(new ChangePasswordRequest("Current1!", "NewStr0ng1!")).IsValid.Should().BeTrue();

    [Fact]
    public void Same_password_fails()
        => _validator.TestValidate(new ChangePasswordRequest("SamePass1", "SamePass1")).IsValid.Should().BeFalse();
}

public sealed class CreateProjectRequestValidatorTests
{
    private readonly CreateProjectRequestValidator _validator = new();

    [Theory]
    [InlineData(null, ProjectStatus.Active)]
    [InlineData("Valid project", ProjectStatus.Active)]
    [InlineData("Valid project", ProjectStatus.Completed)]
    public void Valid_project_passes(string? description, ProjectStatus status)
        => _validator.TestValidate(new CreateProjectRequest("My project", description, status)).IsValid.Should().BeTrue();

    [Fact]
    public void Empty_name_fails()
        => _validator.TestValidate(new CreateProjectRequest("", null, null)).IsValid.Should().BeFalse();

    [Fact]
    public void Archived_status_fails_for_new_projects()
        => _validator.TestValidate(new CreateProjectRequest("P", null, ProjectStatus.Archived)).IsValid.Should().BeFalse();

    [Fact]
    public void Too_long_name_fails()
        => _validator.TestValidate(new CreateProjectRequest(new string('x', Project.MaxNameLength + 1), null, null))
            .IsValid.Should().BeFalse();
}

public sealed class AddProjectMemberRequestValidatorTests
{
    private readonly AddProjectMemberRequestValidator _validator = new();

    [Theory]
    [InlineData("member@example.com", Role.Admin)]
    [InlineData("member@example.com", Role.Member)]
    [InlineData("member@example.com", Role.Viewer)]
    public void Valid_member_request_passes(string email, string role)
        => _validator.TestValidate(new AddProjectMemberRequest(email, role)).IsValid.Should().BeTrue();

    [Fact]
    public void Owner_role_is_not_allowed()
        => _validator.TestValidate(new AddProjectMemberRequest("member@example.com", Role.Owner)).IsValid.Should().BeFalse();

    [Fact]
    public void Invalid_email_fails()
        => _validator.TestValidate(new AddProjectMemberRequest("not-an-email", Role.Member)).IsValid.Should().BeFalse();
}
