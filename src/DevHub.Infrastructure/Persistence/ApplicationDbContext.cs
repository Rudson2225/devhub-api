using DevHub.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace DevHub.Infrastructure.Persistence;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<Role> Roles => Set<Role>();

    public DbSet<Permission> Permissions => Set<Permission>();

    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();

    public DbSet<Project> Projects => Set<Project>();

    public DbSet<ProjectMember> ProjectMembers => Set<ProjectMember>();

    public DbSet<ProjectTask> Tasks => Set<ProjectTask>();

    public DbSet<TaskComment> TaskComments => Set<TaskComment>();

    public DbSet<Tag> Tags => Set<Tag>();

    public DbSet<ProjectTag> ProjectTags => Set<ProjectTag>();

    public DbSet<TaskTag> TaskTags => Set<TaskTag>();

    public DbSet<Document> Documents => Set<Document>();

    public DbSet<ActivityLog> ActivityLogs => Set<ActivityLog>();

    public DbSet<Notification> Notifications => Set<Notification>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
        ApplyGlobalConventions(modelBuilder);
        base.OnModelCreating(modelBuilder);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        TouchTimestamps();
        return base.SaveChangesAsync(cancellationToken);
    }

    public override int SaveChanges()
    {
        TouchTimestamps();
        return base.SaveChanges();
    }

    private void TouchTimestamps()
    {
        var now = DateTime.UtcNow;
        var changedEntities = ChangeTracker.Entries()
            .Where(e => e.Entity is Domain.Common.BaseEntity);

        foreach (var entry in changedEntities)
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Property(nameof(Domain.Common.BaseEntity.CreatedAt)).CurrentValue = now;
                    entry.Property(nameof(Domain.Common.BaseEntity.UpdatedAt)).CurrentValue = now;
                    break;
                case EntityState.Modified:
                    entry.Property(nameof(Domain.Common.BaseEntity.UpdatedAt)).CurrentValue = now;
                    break;
            }
        }
    }

    private static void ApplyGlobalConventions(ModelBuilder modelBuilder)
    {
        foreach (var property in modelBuilder.Model
                     .GetEntityTypes()
                     .SelectMany(t => t.GetProperties())
                     .Where(p => p.ClrType.IsEnum))
        {
            property.SetColumnType("text");
        }
    }
}
