using DevHub.Application.Abstractions;
using DevHub.Application.Common.Exceptions;
using DevHub.Application.DTOs.Documents;
using DevHub.Application.Mapping;
using DevHub.Domain;
using DevHub.Domain.Entities;
using DevHub.Domain.Enums;

namespace DevHub.Application.UseCases;

public interface IDocumentService
{
    Task<IReadOnlyList<DocumentSummaryResponse>> ListByProjectAsync(Guid actorId, Guid projectId, CancellationToken cancellationToken = default);

    Task<DocumentResponse> GetAsync(Guid actorId, Guid documentId, CancellationToken cancellationToken = default);

    Task<DocumentResponse> CreateAsync(Guid actorId, Guid projectId, CreateDocumentRequest request, CancellationToken cancellationToken = default);

    Task<DocumentResponse> UpdateAsync(Guid actorId, Guid documentId, UpdateDocumentRequest request, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid actorId, Guid documentId, CancellationToken cancellationToken = default);
}

public sealed class DocumentService : IDocumentService
{
    private readonly IDocumentRepository _documents;
    private readonly IActivityRepository _activities;
    private readonly IProjectAccessService _access;
    private readonly IUnitOfWork _unitOfWork;

    public DocumentService(
        IDocumentRepository documents,
        IActivityRepository activities,
        IProjectAccessService access,
        IUnitOfWork unitOfWork)
    {
        _documents = documents;
        _activities = activities;
        _access = access;
        _unitOfWork = unitOfWork;
    }

    public async Task<IReadOnlyList<DocumentSummaryResponse>> ListByProjectAsync(Guid actorId, Guid projectId, CancellationToken cancellationToken = default)
    {
        await _access.EnsurePermissionAsync(projectId, Permissions.DocumentsRead, actorId, cancellationToken);

        var documents = await _documents.ListByProjectAsync(projectId, cancellationToken);
        return documents
            .OrderByDescending(d => d.CreatedAt)
            .Select(d => d.ToSummaryResponse())
            .ToList();
    }

    public async Task<DocumentResponse> GetAsync(Guid actorId, Guid documentId, CancellationToken cancellationToken = default)
    {
        var document = await GetAccessibleDocumentAsync(actorId, documentId, Permissions.DocumentsRead, cancellationToken);
        return document.ToResponse();
    }

    public async Task<DocumentResponse> CreateAsync(Guid actorId, Guid projectId, CreateDocumentRequest request, CancellationToken cancellationToken = default)
    {
        await _access.EnsurePermissionAsync(projectId, Permissions.DocumentsManage, actorId, cancellationToken);

        var document = new Document
        {
            ProjectId = projectId,
            AuthorId = actorId,
            Title = request.Title.Trim(),
            Type = request.Type ?? DocumentType.Other,
            Content = request.Content
        };

        await _documents.AddAsync(document, cancellationToken);

        await _activities.AddAsync(new ActivityLog
        {
            ProjectId = projectId,
            UserId = actorId,
            Type = ActivityType.DocumentCreated,
            Message = $"Document '{document.Title}' was created."
        }, cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var saved = await _documents.GetByIdAsync(document.Id, cancellationToken)
            ?? throw new NotFoundException("Document not found.");
        return saved.ToResponse();
    }

    public async Task<DocumentResponse> UpdateAsync(Guid actorId, Guid documentId, UpdateDocumentRequest request, CancellationToken cancellationToken = default)
    {
        var document = await GetAccessibleDocumentAsync(actorId, documentId, Permissions.DocumentsManage, cancellationToken);

        if (request.Title is not null)
        {
            document.Title = request.Title.Trim();
        }

        if (request.Type is not null)
        {
            document.Type = request.Type.Value;
        }

        if (request.Content is not null)
        {
            document.Content = request.Content;
        }

        await _activities.AddAsync(new ActivityLog
        {
            ProjectId = document.ProjectId,
            UserId = actorId,
            Type = ActivityType.DocumentUpdated,
            Message = $"Document '{document.Title}' was updated."
        }, cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var saved = await _documents.GetByIdAsync(document.Id, cancellationToken)
            ?? throw new NotFoundException("Document not found.");
        return saved.ToResponse();
    }

    public async Task DeleteAsync(Guid actorId, Guid documentId, CancellationToken cancellationToken = default)
    {
        var document = await GetAccessibleDocumentAsync(actorId, documentId, Permissions.DocumentsManage, cancellationToken);
        var title = document.Title;
        var projectId = document.ProjectId;

        _documents.Remove(document);

        await _activities.AddAsync(new ActivityLog
        {
            ProjectId = projectId,
            UserId = actorId,
            Type = ActivityType.DocumentDeleted,
            Message = $"Document '{title}' was deleted."
        }, cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private async Task<Document> GetAccessibleDocumentAsync(Guid actorId, Guid documentId, string permission, CancellationToken cancellationToken)
    {
        var document = await _documents.GetByIdAsync(documentId, cancellationToken)
            ?? throw new NotFoundException("Document not found.");

        await _access.EnsurePermissionAsync(document.ProjectId, permission, actorId, cancellationToken);

        return document;
    }
}
