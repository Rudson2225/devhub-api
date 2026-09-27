using DevHub.Application.Abstractions;
using DevHub.Application.Common.Exceptions;
using DevHub.Application.DTOs.Tags;
using DevHub.Application.Mapping;
using DevHub.Domain;
using DevHub.Domain.Entities;
using DevHub.Domain.Enums;

namespace DevHub.Application.UseCases;

public interface ITagService
{
    Task<IReadOnlyList<TagResponse>> ListAsync(Guid actorId, Guid projectId, CancellationToken cancellationToken = default);

    Task<TagResponse> CreateAsync(Guid actorId, Guid projectId, CreateTagRequest request, CancellationToken cancellationToken = default);

    Task<TagResponse> UpdateAsync(Guid actorId, Guid projectId, Guid tagId, UpdateTagRequest request, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid actorId, Guid projectId, Guid tagId, CancellationToken cancellationToken = default);
}

public sealed class TagService : ITagService
{
    private readonly ITagRepository _tags;
    private readonly IActivityRepository _activities;
    private readonly IProjectAccessService _access;
    private readonly IUnitOfWork _unitOfWork;

    public TagService(ITagRepository tags, IActivityRepository activities, IProjectAccessService access, IUnitOfWork unitOfWork)
    {
        _tags = tags;
        _activities = activities;
        _access = access;
        _unitOfWork = unitOfWork;
    }

    public async Task<IReadOnlyList<TagResponse>> ListAsync(Guid actorId, Guid projectId, CancellationToken cancellationToken = default)
    {
        await _access.EnsurePermissionAsync(projectId, Permissions.TagsRead, actorId, cancellationToken);

        var tags = await _tags.ListByProjectAsync(projectId, cancellationToken);
        return tags
            .OrderBy(t => t.Name)
            .Select(t => t.ToResponse())
            .ToList();
    }

    public async Task<TagResponse> CreateAsync(Guid actorId, Guid projectId, CreateTagRequest request, CancellationToken cancellationToken = default)
    {
        await _access.EnsurePermissionAsync(projectId, Permissions.TagsManage, actorId, cancellationToken);

        var name = request.Name.Trim();
        var normalized = name.ToLowerInvariant();

        var existing = await _tags.GetByNameAsync(projectId, normalized, cancellationToken);
        if (existing is not null)
        {
            throw new ConflictException($"A tag named '{name}' is already associated with this project.");
        }

        Tag tag;
        var globalTag = await _tags.GetByNameAsync(null, normalized, cancellationToken);
        if (globalTag is not null)
        {
            // The global tag is shared taxonomy owned by the platform. Reusing it must never let a
            // project admin rewrite its colour or description, which would leak across every other
            // project that already uses the same tag.
            tag = globalTag;
        }
        else
        {
            tag = new Tag
            {
                ProjectId = projectId,
                Name = name,
                Description = request.Description,
                Color = request.Color ?? "#6b7280"
            };
            await _tags.AddAsync(tag, cancellationToken);
        }

        await _tags.AddProjectTagAsync(new ProjectTag
        {
            ProjectId = projectId,
            TagId = tag.Id,
            AddedAt = DateTime.UtcNow
        }, cancellationToken);

        await _activities.AddAsync(new ActivityLog
        {
            ProjectId = projectId,
            UserId = actorId,
            Type = ActivityType.TagAdded,
            Message = $"Tag '{tag.Name}' was added to the project."
        }, cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var saved = await _tags.GetByIdAsync(tag.Id, cancellationToken)
            ?? throw new NotFoundException("Tag not found.");
        return saved.ToResponse();
    }

    public async Task<TagResponse> UpdateAsync(Guid actorId, Guid projectId, Guid tagId, UpdateTagRequest request, CancellationToken cancellationToken = default)
    {
        await _access.EnsurePermissionAsync(projectId, Permissions.TagsManage, actorId, cancellationToken);

        var tag = await _tags.GetByIdAsync(tagId, cancellationToken)
            ?? throw new NotFoundException("Tag not found.");

        if (tag.ProjectId is null)
        {
            // A global tag is shared by every project, so renaming it here would change it for all of
            // them. It exists and is reachable, which is why this is a refusal rather than a 404.
            throw new BadRequestException("Only project-scoped tags can be updated. This tag is a global tag.");
        }

        if (tag.ProjectId != projectId)
        {
            // Same reasoning as DeleteAsync: the tag is not part of this project, so the answer must
            // not confirm that it exists somewhere else, and it must not say "global tag" either -
            // that would be a different situation entirely.
            throw new NotFoundException("Tag not found.");
        }

        if (request.Name is not null)
        {
            var name = request.Name.Trim();
            var existing = await _tags.GetByNameAsync(projectId, name.ToLowerInvariant(), cancellationToken);
            if (existing is not null && existing.Id != tag.Id)
            {
                throw new ConflictException($"A tag named '{name}' is already associated with this project.");
            }

            tag.Name = name;
        }

        if (request.Description is not null)
        {
            tag.Description = request.Description;
        }

        if (request.Color is not null)
        {
            tag.Color = request.Color;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var saved = await _tags.GetByIdAsync(tag.Id, cancellationToken)
            ?? throw new NotFoundException("Tag not found.");
        return saved.ToResponse();
    }

    public async Task DeleteAsync(Guid actorId, Guid projectId, Guid tagId, CancellationToken cancellationToken = default)
    {
        await _access.EnsurePermissionAsync(projectId, Permissions.TagsManage, actorId, cancellationToken);

        var tag = await _tags.GetByIdAsync(tagId, cancellationToken)
            ?? throw new NotFoundException("Tag not found.");

        var isAssociated = await _tags.IsAssociatedWithProjectAsync(projectId, tagId, cancellationToken);
        if (!isAssociated)
        {
            throw new NotFoundException("This tag is not associated with the project.");
        }

        var name = tag.Name;
        var projectTag = await _tags.GetProjectTagAsync(projectId, tagId, cancellationToken)
            ?? throw new NotFoundException("This tag is not associated with the project.");
        _tags.RemoveProjectTag(projectTag);

        await _activities.AddAsync(new ActivityLog
        {
            ProjectId = projectId,
            UserId = actorId,
            Type = ActivityType.TagRemoved,
            Message = $"Tag '{name}' was removed from the project."
        }, cancellationToken);

        // If the tag was project-scoped (not global) and no longer used, delete the tag itself.
        if (tag.ProjectId == projectId)
        {
            _tags.Remove(tag);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
