using DevHub.Application.Common.Exceptions;
using DevHub.Application.DTOs.Projects;
using DevHub.Application.UseCases;
using DevHub.Domain.Entities;
using DevHub.UnitTests.Fakes;
using FluentAssertions;

namespace DevHub.UnitTests.UseCases;

public sealed class ProjectMemberServiceTests
{
    private readonly FakeProjectMemberRepository _members;
    private readonly FakeUserRepository _users = new();
    private readonly FakeRoleRepository _roles = new();
    private readonly FakePermissionRepository _permissions = new();
    private readonly FakeActivityRepository _activities = new();
    private readonly FakeNotificationRepository _notifications = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly ProjectMemberService _service;

    public ProjectMemberServiceTests()
    {
        _members = new FakeProjectMemberRepository(_roles, _users);

        foreach (var role in TestRoles.DefaultRoles())
        {
            _roles.Roles.Add(role);
        }

        _permissions.Permissions.Add(DevHub.Domain.Permissions.MembersManage);

        var access = new ProjectAccessService(_members, _permissions);
        _service = new ProjectMemberService(
            _members,
            _users,
            _roles,
            _activities,
            _notifications,
            access,
            _unitOfWork);
    }

    private const string ProjectOwnerEmail = "owner@example.com";
    private const string ProjectMemberEmail = "member@example.com";

    private (Guid ProjectId, User Owner, User Member, Role OwnerRole) SeedProjectWithOwnerAndMember()
    {
        var projectId = Guid.NewGuid();
        var ownerRole = _roles.Roles.Single(r => r.Name == Role.Owner);
        var adminRole = _roles.Roles.Single(r => r.Name == Role.Admin);

        var owner = TestUsers.NewUser(ProjectOwnerEmail, "Owner");
        var member = TestUsers.NewUser(ProjectMemberEmail, "Member");
        _users.Users.Add(owner);
        _users.Users.Add(member);

        _members.Members.Add(new ProjectMember
        {
            ProjectId = projectId,
            UserId = owner.Id,
            RoleId = ownerRole.Id,
            Role = ownerRole,
            JoinedAt = DateTime.UtcNow
        });
        _members.Members.Add(new ProjectMember
        {
            ProjectId = projectId,
            UserId = member.Id,
            RoleId = adminRole.Id,
            Role = adminRole,
            JoinedAt = DateTime.UtcNow
        });

        return (projectId, owner, member, ownerRole);
    }

    [Fact]
    public async Task Add_creates_membership_and_logs_activity_and_notification()
    {
        var (projectId, owner, _, _) = SeedProjectWithOwnerAndMember();
        var newUser = TestUsers.NewUser("new@example.com", "New");
        _users.Users.Add(newUser);

        var response = await _service.AddAsync(
            owner.Id,
            projectId,
            new AddProjectMemberRequest("new@example.com", Role.Member));

        response.Role.Should().Be(Role.Member);
        _members.Members.Should().HaveCount(3);
        _activities.Activities.Should().ContainSingle(a => a.Type == DevHub.Domain.Enums.ActivityType.MemberAdded);
        _notifications.Notifications.Should().ContainSingle(n => n.UserId == newUser.Id);
    }

    [Fact]
    public async Task Add_unknown_user_throws_not_found()
    {
        var (projectId, owner, _, _) = SeedProjectWithOwnerAndMember();

        var act = () => _service.AddAsync(
            owner.Id,
            projectId,
            new AddProjectMemberRequest("missing@example.com", Role.Member));

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Add_duplicate_membership_throws_conflict()
    {
        var (projectId, owner, member, _) = SeedProjectWithOwnerAndMember();

        var act = () => _service.AddAsync(
            owner.Id,
            projectId,
            new AddProjectMemberRequest(ProjectMemberEmail, Role.Viewer));

        await act.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task UpdateRole_throws_when_target_is_owner()
    {
        var (projectId, owner, _, _) = SeedProjectWithOwnerAndMember();

        var act = () => _service.UpdateRoleAsync(
            owner.Id,
            projectId,
            owner.Id,
            new UpdateMemberRoleRequest(Role.Viewer));

        await act.Should().ThrowAsync<BadRequestException>();
    }

    [Fact]
    public async Task UpdateRole_throws_conflict_when_role_unchanged()
    {
        var (projectId, owner, member, _) = SeedProjectWithOwnerAndMember();

        var act = () => _service.UpdateRoleAsync(
            owner.Id,
            projectId,
            member.Id,
            new UpdateMemberRoleRequest(Role.Admin));

        await act.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task UpdateRole_changes_role_and_notifies()
    {
        var (projectId, owner, member, _) = SeedProjectWithOwnerAndMember();

        var response = await _service.UpdateRoleAsync(
            owner.Id,
            projectId,
            member.Id,
            new UpdateMemberRoleRequest(Role.Viewer));

        response.Role.Should().Be(Role.Viewer);
        _notifications.Notifications.Should().ContainSingle(n => n.UserId == member.Id && n.Type == DevHub.Domain.Enums.NotificationType.RoleChanged);
    }

    [Fact]
    public async Task Remove_owner_throws_bad_request()
    {
        var (projectId, owner, _, _) = SeedProjectWithOwnerAndMember();

        var act = () => _service.RemoveAsync(owner.Id, projectId, owner.Id);
        await act.Should().ThrowAsync<BadRequestException>();
    }

    [Fact]
    public async Task Remove_member_logs_activity()
    {
        var (projectId, owner, member, _) = SeedProjectWithOwnerAndMember();

        await _service.RemoveAsync(owner.Id, projectId, member.Id);

        _members.Members.Should().ContainSingle(m => m.UserId == owner.Id);
        _members.Members.Should().NotContain(m => m.UserId == member.Id);
        _activities.Activities.Should().ContainSingle(a => a.Type == DevHub.Domain.Enums.ActivityType.MemberRemoved);
    }

    [Fact]
    public async Task Non_manager_cannot_add_member()
    {
        var projectId = Guid.NewGuid();
        var viewerRole = _roles.Roles.Single(r => r.Name == Role.Viewer);
        var viewer = TestUsers.NewUser("viewer@example.com", "Viewer");
        _users.Users.Add(viewer);

        _permissions.Permissions.Clear();
        _permissions.Permissions.Add(DevHub.Domain.Permissions.ProjectRead);

        _members.Members.Add(new ProjectMember
        {
            ProjectId = projectId,
            UserId = viewer.Id,
            RoleId = viewerRole.Id,
            Role = viewerRole,
            JoinedAt = DateTime.UtcNow
        });

        var act = () => _service.AddAsync(
            viewer.Id,
            projectId,
            new AddProjectMemberRequest("somebody@example.com", Role.Member));

        await act.Should().ThrowAsync<ForbiddenException>();
    }
}
