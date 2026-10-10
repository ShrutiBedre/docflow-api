namespace DocFlow.Api.Domain;

public enum UserRole
{
    Admin = 0,
    Author = 1,
    Reviewer = 2
}

public enum DocumentStatus
{
    Draft = 0,
    InReview = 1,
    Approved = 2,
    Rejected = 3
}

public class User
{
    public int Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public UserRole Role { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class Project
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int OwnerId { get; set; }
    public User Owner { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
    public ICollection<Document> Documents { get; set; } = new List<Document>();
}

public class Document
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public Project Project { get; set; } = null!;
    public string Title { get; set; } = string.Empty;
    public DocumentStatus Status { get; set; }
    public int CurrentVersion { get; set; }
    public int CreatedById { get; set; }
    public User CreatedBy { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public ICollection<DocumentVersion> Versions { get; set; } = new List<DocumentVersion>();
}

public class DocumentVersion
{
    public int Id { get; set; }
    public int DocumentId { get; set; }
    public Document Document { get; set; } = null!;
    public int VersionNumber { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ChangeSummary { get; set; } = string.Empty;
    public int UploadedById { get; set; }
    public User UploadedBy { get; set; } = null!;
    public DateTime UploadedAt { get; set; }
}

/// <summary>Append-only audit trail of every state-changing action.</summary>
public class ActivityLog
{
    public long Id { get; set; }
    public int? ProjectId { get; set; }
    public Project? Project { get; set; }
    public int? DocumentId { get; set; }
    public Document? Document { get; set; }
    public int UserId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? Details { get; set; }
    public DateTime CreatedAt { get; set; }
}
