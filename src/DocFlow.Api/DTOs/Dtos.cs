using System.ComponentModel.DataAnnotations;
using DocFlow.Api.Domain;

namespace DocFlow.Api.DTOs;

// ---------- Auth ----------
public record RegisterRequest(
    [Required, EmailAddress, MaxLength(256)] string Email,
    [Required, MaxLength(150)] string FullName,
    [Required, MinLength(8), MaxLength(100)] string Password,
    UserRole Role = UserRole.Author);

public record LoginRequest(
    [Required, EmailAddress] string Email,
    [Required] string Password);

public record AuthResponse(string Token, DateTime ExpiresAt, UserDto User);

public record UserDto(int Id, string Email, string FullName, UserRole Role);

// ---------- Projects ----------
public record CreateProjectRequest(
    [Required, MaxLength(200)] string Name,
    [MaxLength(2000)] string? Description);

public record ProjectDto(int Id, string Name, string? Description, int OwnerId, DateTime CreatedAt, int DocumentCount);

// ---------- Documents ----------
public record CreateDocumentRequest(
    [Required, MaxLength(300)] string Title,
    [Required, MaxLength(260)] string FileName);

public record UploadVersionRequest(
    [Required, MaxLength(260)] string FileName,
    [Required, MaxLength(1000)] string ChangeSummary);

public record ReviewRequest([MaxLength(2000)] string? Comment);

public record DocumentDto(
    int Id,
    int ProjectId,
    string Title,
    DocumentStatus Status,
    int CurrentVersion,
    int CreatedById,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public record DocumentVersionDto(int VersionNumber, string FileName, string ChangeSummary, int UploadedById, DateTime UploadedAt);

public record DocumentDetailDto(DocumentDto Document, IReadOnlyList<DocumentVersionDto> Versions);

public record ActivityLogDto(long Id, int UserId, string Action, string? Details, DateTime CreatedAt);

public class DocumentQuery
{
    public int? ProjectId { get; set; }
    public DocumentStatus? Status { get; set; }
    public string? Search { get; set; }

    [Range(1, int.MaxValue)]
    public int Page { get; set; } = 1;

    [Range(1, 100)]
    public int PageSize { get; set; } = 20;
}

// ---------- Mapping (manual, explicit, no runtime reflection) ----------
public static class MappingExtensions
{
    public static UserDto ToDto(this User u) => new(u.Id, u.Email, u.FullName, u.Role);

    public static DocumentDto ToDto(this Document d) =>
        new(d.Id, d.ProjectId, d.Title, d.Status, d.CurrentVersion, d.CreatedById, d.CreatedAt, d.UpdatedAt);

    public static DocumentVersionDto ToDto(this DocumentVersion v) =>
        new(v.VersionNumber, v.FileName, v.ChangeSummary, v.UploadedById, v.UploadedAt);

    public static ActivityLogDto ToDto(this ActivityLog a) =>
        new(a.Id, a.UserId, a.Action, a.Details, a.CreatedAt);
}
