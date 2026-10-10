using DocFlow.Api.Common;
using DocFlow.Api.Data;
using DocFlow.Api.Domain;
using DocFlow.Api.DTOs;
using DocFlow.Api.Repositories;
using DocFlow.Api.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DocFlow.Tests;

public class DocumentServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly DocumentService _service;
    private readonly int _authorId;
    private readonly int _reviewerId;
    private readonly int _projectId;

    public DocumentServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())   // fresh database per test
            .Options;
        _db = new AppDbContext(options);

        var now = DateTime.UtcNow;
        var author = new User { Email = "author@test.com", FullName = "Author", Role = UserRole.Author, CreatedAt = now };
        var reviewer = new User { Email = "reviewer@test.com", FullName = "Reviewer", Role = UserRole.Reviewer, CreatedAt = now };
        _db.Users.AddRange(author, reviewer);
        var project = new Project { Name = "Validation Docs", Owner = author, CreatedAt = now };
        _db.Projects.Add(project);
        _db.SaveChanges();

        _authorId = author.Id;
        _reviewerId = reviewer.Id;
        _projectId = project.Id;

        _service = new DocumentService(
            new DocumentRepository(_db),
            new ProjectRepository(_db),
            new ActivityLogRepository(_db),
            _db,
            TimeProvider.System);
    }

    public void Dispose() => _db.Dispose();

    private Task<DocumentDto> CreateDraftAsync() =>
        _service.CreateAsync(_projectId, new CreateDocumentRequest("SOP-001", "sop-001.pdf"), _authorId);

    [Fact]
    public async Task Create_StartsAsDraftAtVersionOne_AndWritesAudit()
    {
        var doc = await CreateDraftAsync();

        Assert.Equal(DocumentStatus.Draft, doc.Status);
        Assert.Equal(1, doc.CurrentVersion);

        var history = await _service.GetHistoryAsync(doc.Id);
        Assert.Single(history);
        Assert.Equal("DocumentCreated", history[0].Action);
    }

    [Fact]
    public async Task Create_InMissingProject_ThrowsNotFound()
    {
        await Assert.ThrowsAsync<NotFoundException>(() =>
            _service.CreateAsync(999, new CreateDocumentRequest("X", "x.pdf"), _authorId));
    }

    [Fact]
    public async Task SubmitThenApprove_ByDifferentReviewer_Approves()
    {
        var doc = await CreateDraftAsync();

        await _service.SubmitForReviewAsync(doc.Id, _authorId);
        var approved = await _service.ApproveAsync(doc.Id, new ReviewRequest("Looks good"), _reviewerId);

        Assert.Equal(DocumentStatus.Approved, approved.Status);
        var actions = (await _service.GetHistoryAsync(doc.Id)).Select(h => h.Action);
        Assert.Equal(new[] { "DocumentCreated", "SubmittedForReview", "Approved" }, actions);
    }

    [Fact]
    public async Task Submit_ByNonAuthor_ThrowsForbidden()
    {
        var doc = await CreateDraftAsync();

        await Assert.ThrowsAsync<ForbiddenException>(() => _service.SubmitForReviewAsync(doc.Id, _reviewerId));
    }

    [Fact]
    public async Task Approve_OwnDocument_ThrowsForbidden()
    {
        var doc = await CreateDraftAsync();
        await _service.SubmitForReviewAsync(doc.Id, _authorId);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.ApproveAsync(doc.Id, new ReviewRequest(null), _authorId));
    }

    [Fact]
    public async Task Approve_WhenStillDraft_ThrowsBusinessRule()
    {
        var doc = await CreateDraftAsync();

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            _service.ApproveAsync(doc.Id, new ReviewRequest(null), _reviewerId));
    }

    [Fact]
    public async Task Reject_WithoutReason_ThrowsBadRequest()
    {
        var doc = await CreateDraftAsync();
        await _service.SubmitForReviewAsync(doc.Id, _authorId);

        await Assert.ThrowsAsync<BadRequestException>(() =>
            _service.RejectAsync(doc.Id, new ReviewRequest("  "), _reviewerId));
    }

    [Fact]
    public async Task RejectedDocument_NewVersion_ReturnsToDraftAtVersionTwo()
    {
        var doc = await CreateDraftAsync();
        await _service.SubmitForReviewAsync(doc.Id, _authorId);
        await _service.RejectAsync(doc.Id, new ReviewRequest("Missing signatures"), _reviewerId);

        var revised = await _service.UploadNewVersionAsync(
            doc.Id, new UploadVersionRequest("sop-001-v2.pdf", "Added signatures"), _authorId);

        Assert.Equal(DocumentStatus.Draft, revised.Status);
        Assert.Equal(2, revised.CurrentVersion);

        var detail = await _service.GetByIdAsync(doc.Id);
        Assert.Equal(new[] { 2, 1 }, detail.Versions.Select(v => v.VersionNumber));
    }

    [Fact]
    public async Task ApprovedDocument_IsLocked_ForNewVersions()
    {
        var doc = await CreateDraftAsync();
        await _service.SubmitForReviewAsync(doc.Id, _authorId);
        await _service.ApproveAsync(doc.Id, new ReviewRequest(null), _reviewerId);

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            _service.UploadNewVersionAsync(doc.Id, new UploadVersionRequest("late.pdf", "Late change"), _authorId));
    }

    [Fact]
    public async Task Search_FiltersByStatus_AndPaginates()
    {
        for (var i = 0; i < 5; i++)
            await _service.CreateAsync(_projectId, new CreateDocumentRequest($"Doc {i}", $"doc{i}.pdf"), _authorId);
        var submitted = await CreateDraftAsync();
        await _service.SubmitForReviewAsync(submitted.Id, _authorId);

        var drafts = await _service.SearchAsync(new DocumentQuery { Status = DocumentStatus.Draft, Page = 1, PageSize = 2 });

        Assert.Equal(5, drafts.TotalCount);
        Assert.Equal(2, drafts.Items.Count);
        Assert.Equal(3, drafts.TotalPages);
    }
}
