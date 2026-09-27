using DevHub.Application.Abstractions;
using DevHub.Domain;
using DevHub.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DevHub.Infrastructure.Persistence.Seed;

public sealed class DatabaseSeeder
{
    private readonly ApplicationDbContext _context;
    private readonly IPasswordService _passwordService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<DatabaseSeeder> _logger;

    public DatabaseSeeder(
        ApplicationDbContext context,
        IPasswordService passwordService,
        IConfiguration configuration,
        ILogger<DatabaseSeeder> logger)
    {
        _context = context;
        _passwordService = passwordService;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        await SeedRolesAndPermissionsAsync(cancellationToken);
        await SeedDefaultTagsAsync(cancellationToken);
        await SeedDevelopmentAdminAsync(cancellationToken);
    }

    private async Task SeedRolesAndPermissionsAsync(CancellationToken cancellationToken)
    {
        var rolesByName = await _context.Roles
            .Include(r => r.Permissions)
            .ToDictionaryAsync(r => r.Name, cancellationToken);

        var roleDefinitions = new Dictionary<string, string[]>
        {
            [Role.Owner] =
            [
                Permissions.ProjectRead, Permissions.ProjectUpdate, Permissions.ProjectDelete,
                Permissions.MembersManage,
                Permissions.TasksRead, Permissions.TasksCreate, Permissions.TasksUpdate, Permissions.TasksDelete,
                Permissions.CommentsRead, Permissions.CommentsCreate, Permissions.CommentsUpdateOwn, Permissions.CommentsUpdateAny,
                Permissions.CommentsDeleteOwn, Permissions.CommentsDeleteAny,
                Permissions.DocumentsRead, Permissions.DocumentsManage,
                Permissions.TagsRead, Permissions.TagsManage,
                Permissions.ActivitiesRead, Permissions.DashboardRead
            ],
            [Role.Admin] =
            [
                Permissions.ProjectRead, Permissions.ProjectUpdate,
                Permissions.MembersManage,
                Permissions.TasksRead, Permissions.TasksCreate, Permissions.TasksUpdate, Permissions.TasksDelete,
                Permissions.CommentsRead, Permissions.CommentsCreate, Permissions.CommentsUpdateOwn, Permissions.CommentsUpdateAny,
                Permissions.CommentsDeleteOwn, Permissions.CommentsDeleteAny,
                Permissions.DocumentsRead, Permissions.DocumentsManage,
                Permissions.TagsRead, Permissions.TagsManage,
                Permissions.ActivitiesRead, Permissions.DashboardRead
            ],
            [Role.Member] =
            [
                Permissions.ProjectRead,
                Permissions.TasksRead, Permissions.TasksCreate, Permissions.TasksUpdate,
                Permissions.CommentsRead, Permissions.CommentsCreate, Permissions.CommentsUpdateOwn, Permissions.CommentsDeleteOwn,
                Permissions.DocumentsRead,
                Permissions.TagsRead,
                Permissions.ActivitiesRead, Permissions.DashboardRead
            ],
            [Role.Viewer] =
            [
                Permissions.ProjectRead,
                Permissions.TasksRead,
                Permissions.CommentsRead,
                Permissions.DocumentsRead,
                Permissions.TagsRead,
                Permissions.ActivitiesRead, Permissions.DashboardRead
            ]
        };

        var permissionNames = roleDefinitions.Values.SelectMany(v => v).Distinct().ToArray();
        await EnsurePermissionsAsync(permissionNames, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);

        foreach (var (roleName, rolePermissionNames) in roleDefinitions)
        {
            if (!rolesByName.TryGetValue(roleName, out var role))
            {
                role = new Role
                {
                    Name = roleName,
                    Description = $"{roleName} role"
                };
                _context.Roles.Add(role);
                rolesByName[roleName] = role;
            }

            var existing = (await _context.RolePermissions
                    .Where(rp => rp.RoleId == role.Id)
                    .Select(rp => rp.Permission.Name)
                    .ToListAsync(cancellationToken))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var permissionName in rolePermissionNames.Where(name => !existing.Contains(name)).Distinct())
            {
                var permission = await _context.Permissions
                    .FirstAsync(p => p.Name == permissionName, cancellationToken);
                _context.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionId = permission.Id });
            }
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    private async Task EnsurePermissionsAsync(IEnumerable<string> permissionNames, CancellationToken cancellationToken)
    {
        var names = permissionNames.ToArray();
        var existing = await _context.Permissions
            .Where(p => names.Contains(p.Name))
            .Select(p => p.Name)
            .ToListAsync(cancellationToken);

        var missing = names.Except(existing, StringComparer.OrdinalIgnoreCase);
        foreach (var name in missing)
        {
            _context.Permissions.Add(new Permission
            {
                Name = name,
                Description = $"Allows '{name}'"
            });
        }
    }

    private async Task SeedDefaultTagsAsync(CancellationToken cancellationToken)
    {
        var defaults = new (string Name, string Color)[]
        {
            ("backend", "#3b82f6"),
            ("frontend", "#8b5cf6"),
            ("bug", "#ef4444"),
            ("feature", "#22c55e"),
            ("urgent", "#f97316"),
            ("documentation", "#06b6d4")
        };

        foreach (var (name, color) in defaults)
        {
            var exists = await _context.Tags.AnyAsync(t => t.ProjectId == null && t.Name == name, cancellationToken);
            if (!exists)
            {
                _context.Tags.Add(new Tag { ProjectId = null, Name = name, Color = color });
            }
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    private async Task SeedDevelopmentAdminAsync(CancellationToken cancellationToken)
    {
        var createAdmin = _configuration.GetValue<bool>("Seed:CreateAdminUser");
        if (!createAdmin)
        {
            return;
        }

        var email = _configuration["Seed:AdminEmail"];
        var password = _configuration["Seed:AdminPassword"];

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            _logger.LogWarning("Seed:CreateAdminUser is enabled but Seed:AdminEmail/Seed:AdminPassword are not set. Skipping admin creation.");
            return;
        }

        var normalizedEmail = email.Trim().ToLowerInvariant();
        var exists = await _context.Users.AnyAsync(u => u.Email == normalizedEmail, cancellationToken);
        if (exists)
        {
            return;
        }

        _context.Users.Add(new User
        {
            Email = normalizedEmail,
            DisplayName = "Development Admin",
            PasswordHash = _passwordService.Hash(password)
        });

        await _context.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Development admin user created with email {Email}.", normalizedEmail);
    }
}
