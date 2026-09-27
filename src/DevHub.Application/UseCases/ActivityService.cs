using DevHub.Application.Abstractions;
using DevHub.Application.Common;
using DevHub.Application.DTOs.Activities;
using DevHub.Application.Mapping;
using DevHub.Domain;

namespace DevHub.Application.UseCases;

public interface IActivityService
{
    Task<PagedResult<ActivityResponse>> ListAsync(Guid actorId, Guid projectId, int page, int pageSize, CancellationToken cancellationToken = default);
}

public sealed class ActivityService : IActivityService
{
    private readonly IActivityRepository _activities;
    private readonly IProjectAccessService _access;

    public ActivityService(IActivityRepository activities, IProjectAccessService access)
    {
        _activities = activities;
        _access = access;
    }

    public async Task<PagedResult<ActivityResponse>> ListAsync(Guid actorId, Guid projectId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        await _access.EnsurePermissionAsync(projectId, Permissions.ActivitiesRead, actorId, cancellationToken);

        var pagination = PaginationQuery.Normalize(page, pageSize);
        var offset = (pagination.Page - 1) * pagination.PageSize;

        var items = await _activities.ListAsync(projectId, pagination.PageSize, offset, cancellationToken);
        var total = await _activities.CountAsync(projectId, cancellationToken);

        var responses = items.Select(a => a.ToResponse()).ToList();
        return PagedResult<ActivityResponse>.Create(responses, pagination.Page, pagination.PageSize, total);
    }
}
