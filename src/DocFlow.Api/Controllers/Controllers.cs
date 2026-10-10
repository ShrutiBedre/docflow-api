using System.Security.Claims;
using DocFlow.Api.Common;
using DocFlow.Api.DTOs;
using DocFlow.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DocFlow.Api.Controllers;

[ApiController]
[Produces("application/json")]
public abstract class ApiControllerBase : ControllerBase
{
    protected int CurrentUserId =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            ? id
            : throw new UnauthorizedAccessException("User id claim is missing.");
}

[Route("api/auth")]
public class AuthController(IAuthService auth) : ApiControllerBase
{
    /// <summary>Register as an Author or Reviewer.</summary>
    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthResponse>> Register(RegisterRequest request, CancellationToken ct) =>
        Ok(await auth.RegisterAsync(request, ct));

    /// <summary>Log in and receive a JWT.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request, CancellationToken ct) =>
        Ok(await auth.LoginAsync(request, ct));
}

[Route("api/projects")]
[Authorize]
public class ProjectsController(IProjectService projects, IDocumentService documents) : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ProjectDto>>> GetAll(CancellationToken ct) =>
        Ok(await projects.GetAllAsync(ct));

    [HttpPost]
    [Authorize(Roles = "Admin,Author")]
    public async Task<ActionResult<ProjectDto>> Create(CreateProjectRequest request, CancellationToken ct)
    {
        var created = await projects.CreateAsync(request, CurrentUserId, ct);
        return StatusCode(StatusCodes.Status201Created, created);
    }

    /// <summary>Create a document (version 1, Draft) inside a project.</summary>
    [HttpPost("{projectId:int}/documents")]
    [Authorize(Roles = "Admin,Author")]
    public async Task<ActionResult<DocumentDto>> CreateDocument(int projectId, CreateDocumentRequest request, CancellationToken ct)
    {
        var created = await documents.CreateAsync(projectId, request, CurrentUserId, ct);
        return CreatedAtAction(nameof(DocumentsController.GetById), "Documents", new { id = created.Id }, created);
    }
}

[Route("api/documents")]
[Authorize]
public class DocumentsController(IDocumentService documents) : ApiControllerBase
{
    /// <summary>Search documents with filtering and pagination.</summary>
    [HttpGet]
    public async Task<ActionResult<PagedResult<DocumentDto>>> Search([FromQuery] DocumentQuery query, CancellationToken ct) =>
        Ok(await documents.SearchAsync(query, ct));

    [HttpGet("{id:int}")]
    public async Task<ActionResult<DocumentDetailDto>> GetById(int id, CancellationToken ct) =>
        Ok(await documents.GetByIdAsync(id, ct));

    /// <summary>Full audit trail for a document.</summary>
    [HttpGet("{id:int}/history")]
    public async Task<ActionResult<IReadOnlyList<ActivityLogDto>>> GetHistory(int id, CancellationToken ct) =>
        Ok(await documents.GetHistoryAsync(id, ct));

    [HttpPost("{id:int}/versions")]
    [Authorize(Roles = "Admin,Author")]
    public async Task<ActionResult<DocumentDto>> UploadVersion(int id, UploadVersionRequest request, CancellationToken ct) =>
        Ok(await documents.UploadNewVersionAsync(id, request, CurrentUserId, ct));

    [HttpPost("{id:int}/submit")]
    [Authorize(Roles = "Admin,Author")]
    public async Task<ActionResult<DocumentDto>> Submit(int id, CancellationToken ct) =>
        Ok(await documents.SubmitForReviewAsync(id, CurrentUserId, ct));

    [HttpPost("{id:int}/approve")]
    [Authorize(Roles = "Admin,Reviewer")]
    public async Task<ActionResult<DocumentDto>> Approve(int id, ReviewRequest request, CancellationToken ct) =>
        Ok(await documents.ApproveAsync(id, request, CurrentUserId, ct));

    [HttpPost("{id:int}/reject")]
    [Authorize(Roles = "Admin,Reviewer")]
    public async Task<ActionResult<DocumentDto>> Reject(int id, ReviewRequest request, CancellationToken ct) =>
        Ok(await documents.RejectAsync(id, request, CurrentUserId, ct));
}
