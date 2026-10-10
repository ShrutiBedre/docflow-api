using DocFlow.Api.Common;
using DocFlow.Api.Data;
using DocFlow.Api.Domain;
using DocFlow.Api.DTOs;
using Microsoft.EntityFrameworkCore;

namespace DocFlow.Api.Repositories;

// ---------- Interfaces ----------
public interface IUserRepository
{
    Task<User?> GetByEmailAsync(string email, CancellationToken ct = default);
    Task<bool> EmailExistsAsync(string email, CancellationToken ct = default);
    Task AddAsync(User user, CancellationToken ct = default);
}

public interface IProjectRepository
{
    Task<Project?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<bool> ExistsAsync(int id, CancellationToken ct = default);
    Task<IReadOnlyList<ProjectDto>> GetAllAsync(CancellationToken ct = default);
    Task AddAsync(Project project, CancellationToken ct = default);
}

public interface IDocumentRepository
{
    Task<Document?> GetByIdAsync(int id, bool includeVersions = false, CancellationToken ct = default);
    Task<PagedResult<Document>> GetPagedAsync(DocumentQuery query, CancellationToken ct = default);
    Task AddAsync(Document document, CancellationToken ct = default);
}

public interface IActivityLogRepository
{
    Task AddAsync(ActivityLog log, CancellationToken ct = default);
    Task<IReadOnlyList<ActivityLog>> GetForDocumentAsync(int documentId, CancellationToken ct = default);
}

// ---------- EF Core implementations ----------
public class UserRepository(AppDbContext db) : IUserRepository
{
    public Task<User?> GetByEmailAsync(string email, CancellationToken ct = default) =>
        db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);

    public Task<bool> EmailExistsAsync(string email, CancellationToken ct = default) =>
        db.Users.AnyAsync(u => u.Email == email, ct);

    public async Task AddAsync(User user, CancellationToken ct = default) =>
        await db.Users.AddAsync(user, ct);
}

public class ProjectRepository(AppDbContext db) : IProjectRepository
{
    public Task<Project?> GetByIdAsync(int id, CancellationToken ct = default) =>
        db.Projects.FirstOrDefaultAsync(p => p.Id == id, ct);

    public Task<bool> ExistsAsync(int id, CancellationToken ct = default) =>
        db.Projects.AnyAsync(p => p.Id == id, ct);

    public async Task<IReadOnlyList<ProjectDto>> GetAllAsync(CancellationToken ct = default) =>
        await db.Projects
            .AsNoTracking()
            .OrderBy(p => p.Name)
            .Select(p => new ProjectDto(p.Id, p.Name, p.Description, p.OwnerId, p.CreatedAt, p.Documents.Count))
            .ToListAsync(ct);

    public async Task AddAsync(Project project, CancellationToken ct = default) =>
        await db.Projects.AddAsync(project, ct);
}

public class DocumentRepository(AppDbContext db) : IDocumentRepository
{
    public Task<Document?> GetByIdAsync(int id, bool includeVersions = false, CancellationToken ct = default)
    {
        IQueryable<Document> q = db.Documents;
        if (includeVersions)
            q = q.Include(d => d.Versions);
        return q.FirstOrDefaultAsync(d => d.Id == id, ct);
    }

    public async Task<PagedResult<Document>> GetPagedAsync(DocumentQuery query, CancellationToken ct = default)
    {
        IQueryable<Document> q = db.Documents.AsNoTracking();

        if (query.ProjectId is not null)
            q = q.Where(d => d.ProjectId == query.ProjectId);

        if (query.Status is not null)
            q = q.Where(d => d.Status == query.Status);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim().ToLower();
            q = q.Where(d => d.Title.ToLower().Contains(term));
        }

        var total = await q.CountAsync(ct);
        var items = await q
            .OrderByDescending(d => d.UpdatedAt)
            .ThenBy(d => d.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(ct);

        return new PagedResult<Document>(items, query.Page, query.PageSize, total);
    }

    public async Task AddAsync(Document document, CancellationToken ct = default) =>
        await db.Documents.AddAsync(document, ct);
}

public class ActivityLogRepository(AppDbContext db) : IActivityLogRepository
{
    public async Task AddAsync(ActivityLog log, CancellationToken ct = default) =>
        await db.ActivityLogs.AddAsync(log, ct);

    public async Task<IReadOnlyList<ActivityLog>> GetForDocumentAsync(int documentId, CancellationToken ct = default) =>
        await db.ActivityLogs
            .AsNoTracking()
            .Where(a => a.DocumentId == documentId)
            .OrderBy(a => a.CreatedAt)
            .ThenBy(a => a.Id)
            .ToListAsync(ct);
}
