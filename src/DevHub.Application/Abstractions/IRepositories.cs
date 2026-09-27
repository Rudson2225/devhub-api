using DevHub.Domain.Entities;
using DevHub.Domain.Enums;

namespace DevHub.Application.Abstractions;

public interface IUserRepository
{
    Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default);

    Task<bool> ExistsByEmailAsync(string email, CancellationToken cancellationToken = default);

    Task AddAsync(User user, CancellationToken cancellationToken = default);
}

public interface IRefreshTokenRepository
{
    /// <summary>
    /// Resolves a token by hash regardless of its state. Reuse detection needs to observe tokens that
    /// were already rotated or revoked, which an "active only" lookup would hide.
    /// </summary>
    Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken cancellationToken = default);

    Task AddAsync(RefreshToken token, CancellationToken cancellationToken = default);

    void Update(RefreshToken token);

    Task RevokeAllForUserAsync(Guid userId, string revokeReason, CancellationToken cancellationToken = default);
}

public interface IRoleRepository
{
    Task<Role?> GetByNameAsync(string name, CancellationToken cancellationToken = default);

    Task<Role?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Role>> ListWithPermissionsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Role>> ListAsync(CancellationToken cancellationToken = default);
}

public interface IPermissionRepository
{
    Task<HashSet<string>> GetPermissionsForRolesAsync(IReadOnlyList<Guid> roleIds, CancellationToken cancellationToken = default);
}

public interface IProjectRepository
{
    Task<Project?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Project>> GetUserProjectsAsync(Guid userId, CancellationToken cancellationToken = default);

    Task AddAsync(Project project, CancellationToken cancellationToken = default);

    void Remove(Project project);
}

public interface IProjectMemberRepository
{
    Task<ProjectMember?> GetMembershipAsync(Guid projectId, Guid userId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProjectMember>> ListMembersAsync(Guid projectId, CancellationToken cancellationToken = default);

    Task<int> CountAsync(Guid projectId, CancellationToken cancellationToken = default);

    Task AddAsync(ProjectMember member, CancellationToken cancellationToken = default);

    void Remove(ProjectMember member);
}

public interface ITaskRepository
{
    Task<ProjectTask?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<ProjectTask?> GetByIdForProjectAsync(Guid projectId, Guid taskId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProjectTask>> ListAsync(Guid projectId, TaskListQuery query, CancellationToken cancellationToken = default);

    Task<int> CountAsync(Guid projectId, TaskListQuery query, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<(ProjectTaskStatus Status, int Count)>> CountByStatusAsync(Guid projectId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<(ProjectTaskPriority Priority, int Count)>> CountByPriorityAsync(Guid projectId, CancellationToken cancellationToken = default);

    Task<int> CountCriticalAsync(Guid projectId, CancellationToken cancellationToken = default);

    Task<int> CountAllAsync(Guid projectId, CancellationToken cancellationToken = default);

    Task AddAsync(ProjectTask task, CancellationToken cancellationToken = default);

    void Remove(ProjectTask task);
}

public sealed record TaskListQuery(
    string? Search,
    ProjectTaskStatus? Status,
    ProjectTaskPriority? Priority,
    Guid? AssigneeId,
    Guid? TagId,
    string? SortBy,
    string? SortOrder,
    int Page,
    int PageSize);

public interface ITaskCommentRepository
{
    Task<TaskComment?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TaskComment>> ListByTaskAsync(Guid taskId, CancellationToken cancellationToken = default);

    Task<Dictionary<Guid, int>> CountByTaskIdsAsync(IReadOnlyList<Guid> taskIds, CancellationToken cancellationToken = default);

    Task AddAsync(TaskComment comment, CancellationToken cancellationToken = default);

    void Remove(TaskComment comment);
}

public interface ITagRepository
{
    Task<Tag?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Tag>> GetByIdsAsync(IReadOnlyList<Guid> ids, CancellationToken cancellationToken = default);

    Task<Tag?> GetByNameAsync(Guid? projectId, string name, CancellationToken cancellationToken = default);

    Task AddAsync(Tag tag, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Tag>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken = default);

    Task<bool> IsAssociatedWithProjectAsync(Guid projectId, Guid tagId, CancellationToken cancellationToken = default);

    Task<ProjectTag?> GetProjectTagAsync(Guid projectId, Guid tagId, CancellationToken cancellationToken = default);

    Task AddProjectTagAsync(ProjectTag projectTag, CancellationToken cancellationToken = default);

    Task AddTaskTagAsync(TaskTag taskTag, CancellationToken cancellationToken = default);

    void RemoveProjectTag(ProjectTag projectTag);

    /// <summary>
    /// Detaches one tag from a task. The entity handed in is the tracked association itself, so the
    /// removal goes through the change tracker and the task keeps a consistent in-memory view. A bulk
    /// delete by task id would not: it would drop the rows behind the tracker's back and leave the
    /// loaded task still reporting tags the database no longer has.
    /// </summary>
    void RemoveTaskTag(TaskTag taskTag);

    void Remove(Tag tag);
}

public interface IDocumentRepository
{
    Task<Document?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Document>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken = default);

    Task AddAsync(Document document, CancellationToken cancellationToken = default);

    void Remove(Document document);
}

public interface IActivityRepository
{
    Task AddAsync(ActivityLog activity, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ActivityLog>> ListAsync(Guid projectId, int limit, int offset, CancellationToken cancellationToken = default);

    Task<int> CountAsync(Guid projectId, CancellationToken cancellationToken = default);
}

public interface INotificationRepository
{
    Task AddAsync(Notification notification, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Notification>> ListByUserAsync(Guid userId, int page, int pageSize, CancellationToken cancellationToken = default);

    Task<int> CountByUserAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<int> CountUnreadByUserAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<Notification?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task MarkAllReadAsync(Guid userId, DateTime readAt, CancellationToken cancellationToken = default);
}

public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
