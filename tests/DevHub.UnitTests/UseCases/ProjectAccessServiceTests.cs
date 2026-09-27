using DevHub.Application.Common.Exceptions;
using DevHub.Application.UseCases;
using DevHub.Domain;
using DevHub.Domain.Entities;
using DevHub.UnitTests.Fakes;
using FluentAssertions;

namespace DevHub.UnitTests.UseCases;

public sealed class ProjectAccessServiceTests
{
    private readonly FakeProjectMemberRepository _members = new();
    private readonly FakePermissionRepository _permissions = new();
    private readonly ProjectAccessService _service;

    public ProjectAccessServiceTests()
    {
        _service = new ProjectAccessService(_members, _permissions);
    }

    private static ProjectMember Membership(Guid projectId, Guid userId, Role role)
        => new()
        {
            ProjectId = projectId,
            UserId = userId,
            RoleId = role.Id,
            Role = role,
            JoinedAt = DateTime.UtcNow
        };

    [Fact]
    public async Task Non_member_is_rejected()
    {
        var projectId = Guid.NewGuid();
        var role = TestRoles.NewRole(Role.Member);
        _members.Members.Add(Membership(projectId, Guid.NewGuid(), role));
        _permissions.Permissions.Add(Permissions.ProjectRead);

        var act = () => _service.EnsurePermissionAsync(projectId, Permissions.ProjectRead, Guid.NewGuid());

        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task Member_with_permission_passes()
    {
        var projectId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var role = TestRoles.NewRole(Role.Member);
        _members.Members.Add(Membership(projectId, userId, role));
        _permissions.Permissions.Add(Permissions.CommentsCreate);

        await _service.EnsurePermissionAsync(projectId, Permissions.CommentsCreate, userId);
    }

    [Fact]
    public async Task Member_without_permission_is_rejected()
    {
        var projectId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var role = TestRoles.NewRole(Role.Viewer);
        _members.Members.Add(Membership(projectId, userId, role));
        _permissions.Permissions.Add(Permissions.ProjectRead);

        var act = () => _service.EnsurePermissionAsync(projectId, Permissions.TasksDelete, userId);

        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task GetRole_returns_null_for_non_member_and_value_for_member()
    {
        var projectId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        (await _service.GetRoleAsync(projectId, userId)).Should().BeNull();

        var role = TestRoles.NewRole(Role.Member);
        _members.Members.Add(Membership(projectId, userId, role));

        (await _service.GetRoleAsync(projectId, userId)).Should().Be(DevHub.Domain.Enums.ProjectMemberRole.Member);
    }

    [Fact]
    public async Task EnsureProjectMember_requires_membership()
    {
        var projectId = Guid.NewGuid();
        var act = () => _service.EnsureProjectMemberAsync(projectId, Guid.NewGuid());
        await act.Should().ThrowAsync<ForbiddenException>();
    }
}
