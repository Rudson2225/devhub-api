using DevHub.Application.Abstractions;
using DevHub.Domain.Entities;
using DevHub.Domain.Enums;

namespace DevHub.UnitTests.Fakes;

public sealed class FakeUnitOfWork : IUnitOfWork
{
    public int SaveCount { get; private set; }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SaveCount++;
        return Task.FromResult(0);
    }
}

public sealed class FakeUserRepository : IUserRepository
{
    public List<User> Users { get; } = [];

    public Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => Task.FromResult(Users.FirstOrDefault(u => u.Id == id));

    public Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default)
        => Task.FromResult(Users.FirstOrDefault(u => u.Email == Normalize(email)));

    public Task<bool> ExistsByEmailAsync(string email, CancellationToken cancellationToken = default)
        => Task.FromResult(Users.Any(u => u.Email == Normalize(email)));

    public Task AddAsync(User user, CancellationToken cancellationToken = default)
    {
        Users.Add(user);
        return Task.CompletedTask;
    }

    private static string Normalize(string email) => email.Trim().ToLowerInvariant();
}

public sealed class FakeRefreshTokenRepository : IRefreshTokenRepository
{
    public List<RefreshToken> Tokens { get; } = [];

    public Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken cancellationToken = default)
        => Task.FromResult(Tokens.FirstOrDefault(t => t.TokenHash == tokenHash));

    public Task AddAsync(RefreshToken token, CancellationToken cancellationToken = default)
    {
        Tokens.Add(token);
        return Task.CompletedTask;
    }

    public Task RevokeAllForUserAsync(Guid userId, string revokeReason, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        foreach (var token in Tokens.Where(t => t.UserId == userId && t.RevokedAt is null))
        {
            token.RevokedAt = now;
            token.RevokedReason = revokeReason;
        }

        return Task.CompletedTask;
    }
}

public sealed class FakeRoleRepository : IRoleRepository
{
    public List<Role> Roles { get; } = [];

    public Task<Role?> GetByNameAsync(string name, CancellationToken cancellationToken = default)
        => Task.FromResult(Roles.FirstOrDefault(r => r.Name == name));

    public Task<Role?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => Task.FromResult(Roles.FirstOrDefault(r => r.Id == id));

    public Task<IReadOnlyList<Role>> ListWithPermissionsAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<Role>>(Roles.ToList());
}

public sealed class FakeProjectMemberRepository : IProjectMemberRepository
{
    private readonly FakeRoleRepository? _roles;
    private readonly FakeUserRepository? _users;

    public FakeProjectMemberRepository(FakeRoleRepository? roles = null, FakeUserRepository? users = null)
    {
        _roles = roles;
        _users = users;
    }

    public List<ProjectMember> Members { get; } = [];

    public Task<ProjectMember?> GetMembershipAsync(Guid projectId, Guid userId, CancellationToken cancellationToken = default)
    {
        var member = Members.FirstOrDefault(m => m.ProjectId == projectId && m.UserId == userId);
        if (member is null)
        {
            return Task.FromResult<ProjectMember?>(null);
        }

        if (_roles is not null)
        {
            member.Role = _roles.Roles.FirstOrDefault(r => r.Id == member.RoleId)
                ?? member.Role
                ?? new Role { Id = member.RoleId, Name = "Unknown" };
        }
        else
        {
            member.Role ??= new Role { Id = member.RoleId, Name = "Unknown" };
        }

        member.User ??= _users?.Users.FirstOrDefault(u => u.Id == member.UserId)
            ?? new User { Id = member.UserId, DisplayName = "Unknown", Email = "unknown@example.com" };

        return Task.FromResult<ProjectMember?>(member);
    }

    public Task<IReadOnlyList<ProjectMember>> ListMembersAsync(Guid projectId, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<ProjectMember>>(
            Members.Where(m => m.ProjectId == projectId).ToList());

    public Task<int> CountAsync(Guid projectId, CancellationToken cancellationToken = default)
        => Task.FromResult(Members.Count(m => m.ProjectId == projectId));

    public Task AddAsync(ProjectMember member, CancellationToken cancellationToken = default)
    {
        Members.Add(member);
        return Task.CompletedTask;
    }

    public void Remove(ProjectMember member) => Members.Remove(member);
}

public sealed class FakePermissionRepository : IPermissionRepository
{
    public HashSet<string> Permissions { get; set; } = new(StringComparer.Ordinal);

    public Task<HashSet<string>> GetPermissionsForRolesAsync(IReadOnlyList<Guid> roleIds, CancellationToken cancellationToken = default)
        => Task.FromResult(new HashSet<string>(Permissions, StringComparer.Ordinal));
}

public sealed class FakeActivityRepository : IActivityRepository
{
    public List<ActivityLog> Activities { get; } = [];

    public Task AddAsync(ActivityLog activity, CancellationToken cancellationToken = default)
    {
        Activities.Add(activity);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ActivityLog>> ListAsync(Guid projectId, int limit, int offset, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<ActivityLog>>(
            Activities.Where(a => a.ProjectId == projectId).Skip(offset).Take(limit).ToList());

    public Task<int> CountAsync(Guid projectId, CancellationToken cancellationToken = default)
        => Task.FromResult(Activities.Count(a => a.ProjectId == projectId));
}

public sealed class FakeNotificationRepository : INotificationRepository
{
    public List<Notification> Notifications { get; } = [];

    public Task AddAsync(Notification notification, CancellationToken cancellationToken = default)
    {
        Notifications.Add(notification);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Notification>> ListByUserAsync(Guid userId, int page, int pageSize, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<Notification>>(Notifications
            .Where(n => n.UserId == userId)
            .OrderByDescending(n => n.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList());

    public Task<int> CountByUserAsync(Guid userId, CancellationToken cancellationToken = default)
        => Task.FromResult(Notifications.Count(n => n.UserId == userId));

    public Task<int> CountUnreadByUserAsync(Guid userId, CancellationToken cancellationToken = default)
        => Task.FromResult(Notifications.Count(n => n.UserId == userId && !n.IsRead));

    public Task<Notification?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => Task.FromResult(Notifications.FirstOrDefault(n => n.Id == id));

    public Task MarkAllReadAsync(Guid userId, DateTime readAt, CancellationToken cancellationToken = default)
    {
        foreach (var notification in Notifications.Where(n => n.UserId == userId && !n.IsRead))
        {
            notification.IsRead = true;
            notification.ReadAt = readAt;
        }

        return Task.CompletedTask;
    }
}

public sealed class FakeAccessTokenGenerator : IAccessTokenGenerator
{
    public string Generate(User user, TimeSpan lifetime) => "access-token";
}

public static class TestUsers
{
    public static User NewUser(string email = "dev@example.com", string displayName = "Dev")
        => new()
        {
            Id = Guid.NewGuid(),
            Email = email,
            DisplayName = displayName,
            PasswordHash = "hash"
        };
}

public static class TestRoles
{
    public static Role NewRole(string name, string? description = null)
        => new() { Id = Guid.NewGuid(), Name = name, Description = description };

    public static IReadOnlyList<Role> DefaultRoles() =>
    [
        NewRole(Role.Owner, "Owns the project"),
        NewRole(Role.Admin, "Manages the project"),
        NewRole(Role.Member, "Works on the project"),
        NewRole(Role.Viewer, "Reads the project")
    ];
}
