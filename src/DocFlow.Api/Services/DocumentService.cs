using DocFlow.Api.Common;
using DocFlow.Api.Data;
using DocFlow.Api.Domain;
using DocFlow.Api.DTOs;
using DocFlow.Api.Repositories;

namespace DocFlow.Api.Services;

public interface IDocumentService
{
    Task<DocumentDto> CreateAsync(int projectId, CreateDocumentRequest request, int userId, CancellationToken ct = default);
    Task<DocumentDetailDto> GetByIdAsync(int id, CancellationToken ct = default);
    Task<PagedResult<DocumentDto>> SearchAsync(DocumentQuery query, CancellationToken ct = default);
    Task<DocumentDto> UploadNewVersionAsync(int id, UploadVersionRequest request, int userId, CancellationToken ct = default);
    Task<DocumentDto> SubmitForReviewAsync(int id, int userId, CancellationToken ct = default);
    Task<DocumentDto> ApproveAsync(int id, ReviewRequest request, int reviewerId, CancellationToken ct = default);
    Task<DocumentDto> RejectAsync(int id, ReviewRequest request, int reviewerId, CancellationToken ct = default);
    Task<IReadOnlyList<ActivityLogDto>> GetHistoryAsync(int id, CancellationToken ct = default);
}

/// <summary>
/// Document lifecycle:
///   Draft --submit--> InReview --approve--> Approved (locked)
///                         |
///                         +--reject--> Rejected --new version--> Draft
/// Rules:
///   * Only the document's author can submit it for review.
///   * A reviewer can never approve or reject their own document (segregation of duties).
///   * New versions are allowed only in Draft or Rejected; Approved documents are immutable.
///   * Every state change writes an audit entry in the same transaction.
/// </summary>
public class DocumentService(
    IDocumentRepository documents,
    IProjectRepository projects,
    IActivityLogRepository logs,
    IUnitOfWork uow,
    TimeProvider clock) : IDocumentService
{
    public async Task<DocumentDto> CreateAsync(int projectId, CreateDocumentRequest request, int userId, CancellationToken ct = default)
    {
        if (!await projects.ExistsAsync(projectId, ct))
            throw new NotFoundException($"Project {projectId} was not found.");

        var now = Now();
        var document = new Document
        {
            ProjectId = projectId,
            Title = request.Title.Trim(),
            Status = DocumentStatus.Draft,
            CurrentVersion = 1,
            CreatedById = userId,
            CreatedAt = now,
            UpdatedAt = now
        };
        document.Versions.Add(new DocumentVersion
        {
            VersionNumber = 1,
            FileName = request.FileName.Trim(),
            ChangeSummary = "Initial version",
            UploadedById = userId,
            UploadedAt = now
        });

        await documents.AddAsync(document, ct);
        await LogAsync(document, userId, "DocumentCreated", $"v1: {request.FileName}", ct);
        await uow.SaveChangesAsync(ct);

        return document.ToDto();
    }

    public async Task<DocumentDetailDto> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var document = await GetOrThrowAsync(id, includeVersions: true, ct);
        var versions = document.Versions
            .OrderByDescending(v => v.VersionNumber)
            .Select(v => v.ToDto())
            .ToList();
        return new DocumentDetailDto(document.ToDto(), versions);
    }

    public async Task<PagedResult<DocumentDto>> SearchAsync(DocumentQuery query, CancellationToken ct = default)
    {
        var page = await documents.GetPagedAsync(query, ct);
        return new PagedResult<DocumentDto>(
            page.Items.Select(d => d.ToDto()).ToList(), page.Page, page.PageSize, page.TotalCount);
    }

    public async Task<DocumentDto> UploadNewVersionAsync(int id, UploadVersionRequest request, int userId, CancellationToken ct = default)
    {
        var document = await GetOrThrowAsync(id, includeVersions: false, ct);

        if (document.Status is not (DocumentStatus.Draft or DocumentStatus.Rejected))
            throw new BusinessRuleException(
                $"Cannot add a version while the document is {document.Status}. Only Draft or Rejected documents can be revised.");

        var now = Now();
        document.CurrentVersion += 1;
        document.Versions.Add(new DocumentVersion
        {
            VersionNumber = document.CurrentVersion,
            FileName = request.FileName.Trim(),
            ChangeSummary = request.ChangeSummary.Trim(),
            UploadedById = userId,
            UploadedAt = now
        });
        document.Status = DocumentStatus.Draft;   // a revised document starts a fresh review cycle
        document.UpdatedAt = now;

        await LogAsync(document, userId, "VersionUploaded", $"v{document.CurrentVersion}: {request.ChangeSummary}", ct);
        await uow.SaveChangesAsync(ct);

        return document.ToDto();
    }

    public async Task<DocumentDto> SubmitForReviewAsync(int id, int userId, CancellationToken ct = default)
    {
        var document = await GetOrThrowAsync(id, includeVersions: false, ct);

        if (document.CreatedById != userId)
            throw new ForbiddenException("Only the document's author can submit it for review.");

        EnsureStatus(document, DocumentStatus.Draft, "submit for review");

        return await TransitionAsync(document, DocumentStatus.InReview, userId, "SubmittedForReview", null, ct);
    }

    public async Task<DocumentDto> ApproveAsync(int id, ReviewRequest request, int reviewerId, CancellationToken ct = default)
    {
        var document = await GetOrThrowAsync(id, includeVersions: false, ct);
        EnsureNotSelfReview(document, reviewerId);
        EnsureStatus(document, DocumentStatus.InReview, "approve");

        return await TransitionAsync(document, DocumentStatus.Approved, reviewerId, "Approved", request.Comment, ct);
    }

    public async Task<DocumentDto> RejectAsync(int id, ReviewRequest request, int reviewerId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Comment))
            throw new BadRequestException("A reason is required when rejecting a document.");

        var document = await GetOrThrowAsync(id, includeVersions: false, ct);
        EnsureNotSelfReview(document, reviewerId);
        EnsureStatus(document, DocumentStatus.InReview, "reject");

        return await TransitionAsync(document, DocumentStatus.Rejected, reviewerId, "Rejected", request.Comment, ct);
    }

    public async Task<IReadOnlyList<ActivityLogDto>> GetHistoryAsync(int id, CancellationToken ct = default)
    {
        await GetOrThrowAsync(id, includeVersions: false, ct);
        var entries = await logs.GetForDocumentAsync(id, ct);
        return entries.Select(e => e.ToDto()).ToList();
    }

    // ---------- helpers ----------

    private async Task<DocumentDto> TransitionAsync(
        Document document, DocumentStatus to, int userId, string action, string? details, CancellationToken ct)
    {
        document.Status = to;
        document.UpdatedAt = Now();
        await LogAsync(document, userId, action, details, ct);
        await uow.SaveChangesAsync(ct);
        return document.ToDto();
    }

    private async Task<Document> GetOrThrowAsync(int id, bool includeVersions, CancellationToken ct) =>
        await documents.GetByIdAsync(id, includeVersions, ct)
        ?? throw new NotFoundException($"Document {id} was not found.");

    private static void EnsureStatus(Document document, DocumentStatus expected, string operation)
    {
        if (document.Status != expected)
            throw new BusinessRuleException(
                $"Cannot {operation}: document is {document.Status}, expected {expected}.");
    }

    private static void EnsureNotSelfReview(Document document, int reviewerId)
    {
        if (document.CreatedById == reviewerId)
            throw new ForbiddenException("You cannot review a document you authored.");
    }

    private Task LogAsync(Document document, int userId, string action, string? details, CancellationToken ct) =>
        logs.AddAsync(new ActivityLog
        {
            ProjectId = document.ProjectId,
            Document = document,
            UserId = userId,
            Action = action,
            Details = details,
            CreatedAt = Now()
        }, ct);

    private DateTime Now() => clock.GetUtcNow().UtcDateTime;
}
