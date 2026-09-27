using DevHub.Application.Abstractions;
using DevHub.Domain.Entities;
using DevHub.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace DevHub.Infrastructure.Persistence.Repositories;

public sealed class UserRepository : IUserRepository
{
    private readonly ApplicationDbContext _context;

    public UserRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => await _context.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);

    public async Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default)
        => await _context.Users.FirstOrDefaultAsync(u => u.Email == email.Trim().ToLowerInvariant(), cancellationToken);

    public Task<bool> ExistsByEmailAsync(string email, CancellationToken cancellationToken = default)
        => _context.Users.AnyAsync(u => u.Email == email.Trim().ToLowerInvariant(), cancellationToken);

    public async Task AddAsync(User user, CancellationToken cancellationToken = default)
        => await _context.Users.AddAsync(user, cancellationToken);
}

public sealed class RefreshTokenRepository : IRefreshTokenRepository
{
    private readonly ApplicationDbContext _context;

    public RefreshTokenRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken cancellationToken = default)
        => _context.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == tokenHash, cancellationToken);

    public async Task AddAsync(RefreshToken token, CancellationToken cancellationToken = default)
        => await _context.RefreshTokens.AddAsync(token, cancellationToken);

    public void Update(RefreshToken token)
        => _context.RefreshTokens.Update(token);

    public async Task RevokeAllForUserAsync(Guid userId, string revokeReason, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        await _context.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.RevokedAt, now)
                .SetProperty(t => t.RevokedByIp, revokeReason),
                cancellationToken);
    }
}

public sealed class RoleRepository : IRoleRepository
{
    private readonly ApplicationDbContext _context;

    public RoleRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public Task<Role?> GetByNameAsync(string name, CancellationToken cancellationToken = default)
        => _context.Roles.FirstOrDefaultAsync(r => r.Name == name, cancellationToken);

    public Task<Role?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => _context.Roles.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Role>> ListAsync(CancellationToken cancellationToken = default)
        => await _context.Roles.AsNoTracking().ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Role>> ListWithPermissionsAsync(CancellationToken cancellationToken = default)
        => await _context.Roles
            .AsNoTracking()
            .Include(r => r.Permissions)
            .ThenInclude(rp => rp.Permission)
            .ToListAsync(cancellationToken);
}

public sealed class PermissionRepository : IPermissionRepository
{
    private readonly ApplicationDbContext _context;

    public PermissionRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<HashSet<string>> GetPermissionsForRolesAsync(IReadOnlyList<Guid> roleIds, CancellationToken cancellationToken = default)
    {
        if (roleIds.Count == 0)
        {
            return new HashSet<string>();
        }

        var names = await _context.RolePermissions
            .AsNoTracking()
            .Where(rp => roleIds.Contains(rp.RoleId))
            .Select(rp => rp.Permission.Name)
            .ToListAsync(cancellationToken);

        return names.ToHashSet();
    }
}

public sealed class ProjectRepository : IProjectRepository
{
    private readonly ApplicationDbContext _context;

    public ProjectRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public Task<Project?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => _context.Projects
            .Include(p => p.Owner)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Project>> GetUserProjectsAsync(Guid userId, CancellationToken cancellationToken = default)
        => await _context.Projects
            .AsNoTracking()
            .Where(p => p.Members.Any(m => m.UserId == userId))
            .ToListAsync(cancellationToken);

    public async Task AddAsync(Project project, CancellationToken cancellationToken = default)
        => await _context.Projects.AddAsync(project, cancellationToken);

    public void Remove(Project project)
        => _context.Projects.Remove(project);
}

public sealed class ProjectMemberRepository : IProjectMemberRepository
{
    private readonly ApplicationDbContext _context;

    public ProjectMemberRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<ProjectMember?> GetMembershipAsync(Guid projectId, Guid userId, CancellationToken cancellationToken = default)
        => await _context.ProjectMembers
            .Include(pm => pm.User)
            .Include(pm => pm.Role)
            .FirstOrDefaultAsync(pm => pm.ProjectId == projectId && pm.UserId == userId, cancellationToken);

    public async Task<IReadOnlyList<ProjectMember>> ListMembersAsync(Guid projectId, CancellationToken cancellationToken = default)
        => await _context.ProjectMembers
            .AsNoTracking()
            .Include(pm => pm.User)
            .Include(pm => pm.Role)
            .Where(pm => pm.ProjectId == projectId)
            .ToListAsync(cancellationToken);

    public Task<int> CountAsync(Guid projectId, CancellationToken cancellationToken = default)
        => _context.ProjectMembers.CountAsync(pm => pm.ProjectId == projectId, cancellationToken);

    public async Task AddAsync(ProjectMember member, CancellationToken cancellationToken = default)
        => await _context.ProjectMembers.AddAsync(member, cancellationToken);

    public void Remove(ProjectMember member)
        => _context.ProjectMembers.Remove(member);
}

public sealed class TaskRepository : ITaskRepository
{
    private readonly ApplicationDbContext _context;

    public TaskRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    private IQueryable<ProjectTask> BaseQuery()
        => _context.Tasks
            .Include(t => t.Assignee)
            .Include(t => t.Reporter)
            .Include(t => t.Tags)
            .ThenInclude(tt => tt.Tag);

    public Task<ProjectTask?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => BaseQuery().FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

    public Task<ProjectTask?> GetByIdForProjectAsync(Guid projectId, Guid taskId, CancellationToken cancellationToken = default)
        => BaseQuery().FirstOrDefaultAsync(t => t.ProjectId == projectId && t.Id == taskId, cancellationToken);

    public async Task<IReadOnlyList<ProjectTask>> ListAsync(Guid projectId, TaskListQuery query, CancellationToken cancellationToken = default)
    {
        var filtered = ApplyFilters(_context.Tasks.AsNoTracking(), projectId, query);
        var ordered = ApplySorting(filtered, query.SortBy, query.SortOrder);

        return await ordered
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Include(t => t.Assignee)
            .Include(t => t.Reporter)
            .Include(t => t.Tags)
            .ThenInclude(tt => tt.Tag)
            .ToListAsync<ProjectTask>(cancellationToken);
    }

    public Task<int> CountAsync(Guid projectId, TaskListQuery query, CancellationToken cancellationToken = default)
        => ApplyFilters(_context.Tasks.AsNoTracking(), projectId, query).CountAsync(cancellationToken);

    public async Task<IReadOnlyList<(ProjectTaskStatus Status, int Count)>> CountByStatusAsync(Guid projectId, CancellationToken cancellationToken = default)
        => await _context.Tasks
            .AsNoTracking()
            .Where(t => t.ProjectId == projectId)
            .GroupBy(t => t.Status)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken)
            .ContinueWith(t => (IReadOnlyList<(ProjectTaskStatus, int)>)t.Result.Select(x => (x.Key, x.Count)).ToList(), cancellationToken);

    public async Task<IReadOnlyList<(ProjectTaskPriority Priority, int Count)>> CountByPriorityAsync(Guid projectId, CancellationToken cancellationToken = default)
        => await _context.Tasks
            .AsNoTracking()
            .Where(t => t.ProjectId == projectId)
            .GroupBy(t => t.Priority)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken)
            .ContinueWith(t => (IReadOnlyList<(ProjectTaskPriority, int)>)t.Result.Select(x => (x.Key, x.Count)).ToList(), cancellationToken);

    public Task<int> CountCriticalAsync(Guid projectId, CancellationToken cancellationToken = default)
        => _context.Tasks.CountAsync(
            t => t.ProjectId == projectId
                 && t.Priority == ProjectTaskPriority.Critical
                 && t.Status != ProjectTaskStatus.Done,
            cancellationToken);

    public Task<int> CountAllAsync(Guid projectId, CancellationToken cancellationToken = default)
        => _context.Tasks.CountAsync(t => t.ProjectId == projectId, cancellationToken);

    public async Task AddAsync(ProjectTask task, CancellationToken cancellationToken = default)
        => await _context.Tasks.AddAsync(task, cancellationToken);

    public void Remove(ProjectTask task)
        => _context.Tasks.Remove(task);

    private static IQueryable<ProjectTask> ApplyFilters(IQueryable<ProjectTask> query, Guid projectId, TaskListQuery f)
    {
        query = query.Where(t => t.ProjectId == projectId);

        if (!string.IsNullOrWhiteSpace(f.Search))
        {
            var search = f.Search.Trim();
            query = query.Where(t => t.Title.Contains(search) || (t.Description != null && t.Description.Contains(search)));
        }

        if (f.Status is not null)
        {
            query = query.Where(t => t.Status == f.Status);
        }

        if (f.Priority is not null)
        {
            query = query.Where(t => t.Priority == f.Priority);
        }

        if (f.AssigneeId is not null)
        {
            query = query.Where(t => t.AssigneeId == f.AssigneeId);
        }

        if (f.TagId is not null)
        {
            query = query.Where(t => t.Tags.Any(tt => tt.TagId == f.TagId));
        }

        return query;
    }

    private static IQueryable<ProjectTask> ApplySorting(IQueryable<ProjectTask> query, string? sortBy, string? sortOrder)
    {
        var descending = string.Equals(sortOrder, "desc", StringComparison.OrdinalIgnoreCase);

        return (sortBy?.ToLowerInvariant()) switch
        {
            "title" => descending ? query.OrderByDescending(t => t.Title) : query.OrderBy(t => t.Title),
            "status" => descending ? query.OrderByDescending(t => t.Status) : query.OrderBy(t => t.Status),
            "priority" => descending ? query.OrderByDescending(t => t.Priority) : query.OrderBy(t => t.Priority),
            "duedate" => descending ? query.OrderByDescending(t => t.DueDate) : query.OrderBy(t => t.DueDate),
            "createdat" => descending ? query.OrderByDescending(t => t.CreatedAt) : query.OrderBy(t => t.CreatedAt),
            _ => descending ? query.OrderByDescending(t => t.UpdatedAt) : query.OrderBy(t => t.UpdatedAt)
        };
    }
}

public sealed class TaskCommentRepository : ITaskCommentRepository
{
    private readonly ApplicationDbContext _context;

    public TaskCommentRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public Task<TaskComment?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => _context.TaskComments
            .Include(c => c.Author)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public async Task<IReadOnlyList<TaskComment>> ListByTaskAsync(Guid taskId, CancellationToken cancellationToken = default)
        => await _context.TaskComments
            .AsNoTracking()
            .Include(c => c.Author)
            .Where(c => c.TaskId == taskId)
            .ToListAsync(cancellationToken);

    public async Task<Dictionary<Guid, int>> CountByTaskIdsAsync(IReadOnlyList<Guid> taskIds, CancellationToken cancellationToken = default)
    {
        if (taskIds.Count == 0)
        {
            return new Dictionary<Guid, int>();
        }

        return await _context.TaskComments
            .AsNoTracking()
            .Where(c => taskIds.Contains(c.TaskId))
            .GroupBy(c => c.TaskId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken);
    }

    public async Task AddAsync(TaskComment comment, CancellationToken cancellationToken = default)
        => await _context.TaskComments.AddAsync(comment, cancellationToken);

    public void Remove(TaskComment comment)
        => _context.TaskComments.Remove(comment);
}

public sealed class TagRepository : ITagRepository
{
    private readonly ApplicationDbContext _context;

    public TagRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public Task<Tag?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => _context.Tags.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Tag>> GetByIdsAsync(IReadOnlyList<Guid> ids, CancellationToken cancellationToken = default)
    {
        if (ids.Count == 0)
        {
            return Array.Empty<Tag>();
        }

        return await _context.Tags.AsNoTracking().Where(t => ids.Contains(t.Id)).ToListAsync<Tag>(cancellationToken);
    }

    public async Task<Tag?> GetByNameAsync(Guid? projectId, string name, CancellationToken cancellationToken = default)
    {
        var normalized = name.Trim().ToLowerInvariant();
        return await _context.Tags.FirstOrDefaultAsync(
            t => t.ProjectId == projectId && t.Name.ToLower() == normalized,
            cancellationToken);
    }

    public async Task AddAsync(Tag tag, CancellationToken cancellationToken = default)
        => await _context.Tags.AddAsync(tag, cancellationToken);

    public async Task<IReadOnlyList<Tag>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken = default)
        => await _context.Tags
            .AsNoTracking()
            .Where(t => t.ProjectId == projectId || t.Projects.Any(pt => pt.ProjectId == projectId))
            .OrderBy(t => t.Name)
            .ToListAsync(cancellationToken);

    public Task<bool> IsAssociatedWithProjectAsync(Guid projectId, Guid tagId, CancellationToken cancellationToken = default)
        => _context.ProjectTags.AnyAsync(pt => pt.ProjectId == projectId && pt.TagId == tagId, cancellationToken);

    public Task<ProjectTag?> GetProjectTagAsync(Guid projectId, Guid tagId, CancellationToken cancellationToken = default)
        => _context.ProjectTags.FirstOrDefaultAsync(pt => pt.ProjectId == projectId && pt.TagId == tagId, cancellationToken);

    public async Task AddProjectTagAsync(ProjectTag projectTag, CancellationToken cancellationToken = default)
    {
        var exists = await _context.ProjectTags.AnyAsync(pt => pt.ProjectId == projectTag.ProjectId && pt.TagId == projectTag.TagId, cancellationToken);
        if (!exists)
        {
            await _context.ProjectTags.AddAsync(projectTag, cancellationToken);
        }
    }

    public async Task AddTaskTagAsync(TaskTag taskTag, CancellationToken cancellationToken = default)
    {
        var exists = await _context.TaskTags.AnyAsync(tt => tt.TaskId == taskTag.TaskId && tt.TagId == taskTag.TagId, cancellationToken);
        if (!exists)
        {
            await _context.TaskTags.AddAsync(taskTag, cancellationToken);
        }
    }

    public void RemoveProjectTag(ProjectTag projectTag)
        => _context.ProjectTags.Remove(projectTag);

    public void RemoveTaskTag(TaskTag taskTag)
        => _context.TaskTags.Remove(taskTag);

    public void Remove(Tag tag)
        => _context.Tags.Remove(tag);
}

public sealed class DocumentRepository : IDocumentRepository
{
    private readonly ApplicationDbContext _context;

    public DocumentRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public Task<Document?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => _context.Documents.Include(d => d.Author).FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Document>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken = default)
        => await _context.Documents
            .AsNoTracking()
            .Include(d => d.Author)
            .Where(d => d.ProjectId == projectId)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(Document document, CancellationToken cancellationToken = default)
        => await _context.Documents.AddAsync(document, cancellationToken);

    public void Remove(Document document)
        => _context.Documents.Remove(document);
}

public sealed class ActivityRepository : IActivityRepository
{
    private readonly ApplicationDbContext _context;

    public ActivityRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task AddAsync(ActivityLog activity, CancellationToken cancellationToken = default)
        => await _context.ActivityLogs.AddAsync(activity, cancellationToken);

    public async Task<IReadOnlyList<ActivityLog>> ListAsync(Guid projectId, int limit, int offset, CancellationToken cancellationToken = default)
        => await _context.ActivityLogs
            .AsNoTracking()
            .Include(a => a.User)
            .Where(a => a.ProjectId == projectId)
            .OrderByDescending(a => a.CreatedAt)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(cancellationToken);

    public Task<int> CountAsync(Guid projectId, CancellationToken cancellationToken = default)
        => _context.ActivityLogs.CountAsync(a => a.ProjectId == projectId, cancellationToken);
}

public sealed class NotificationRepository : INotificationRepository
{
    private readonly ApplicationDbContext _context;

    public NotificationRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task AddAsync(Notification notification, CancellationToken cancellationToken = default)
        => await _context.Notifications.AddAsync(notification, cancellationToken);

    public async Task<IReadOnlyList<Notification>> ListByUserAsync(Guid userId, int page, int pageSize, CancellationToken cancellationToken = default)
        => await _context.Notifications
            .AsNoTracking()
            .Where(n => n.UserId == userId)
            .OrderByDescending(n => n.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

    public Task<int> CountByUserAsync(Guid userId, CancellationToken cancellationToken = default)
        => _context.Notifications.CountAsync(n => n.UserId == userId, cancellationToken);

    public Task<int> CountUnreadByUserAsync(Guid userId, CancellationToken cancellationToken = default)
        => _context.Notifications.CountAsync(n => n.UserId == userId && !n.IsRead, cancellationToken);

    public Task<Notification?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => _context.Notifications.FirstOrDefaultAsync(n => n.Id == id, cancellationToken);

    public async Task MarkAllReadAsync(Guid userId, DateTime readAt, CancellationToken cancellationToken = default)
    {
        await _context.Notifications
            .Where(n => n.UserId == userId && !n.IsRead)
            .ExecuteUpdateAsync(s => s
                .SetProperty(n => n.IsRead, true)
                .SetProperty(n => n.ReadAt, readAt),
                cancellationToken);
    }
}

public sealed class UnitOfWork : IUnitOfWork
{
    private readonly ApplicationDbContext _context;

    public UnitOfWork(ApplicationDbContext context)
    {
        _context = context;
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        => _context.SaveChangesAsync(cancellationToken);
}
