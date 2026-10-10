using DocFlow.Api.Data;
using DocFlow.Api.Domain;
using DocFlow.Api.DTOs;
using DocFlow.Api.Repositories;

namespace DocFlow.Api.Services;

public interface IProjectService
{
    Task<ProjectDto> CreateAsync(CreateProjectRequest request, int userId, CancellationToken ct = default);
    Task<IReadOnlyList<ProjectDto>> GetAllAsync(CancellationToken ct = default);
}

public class ProjectService(
    IProjectRepository projects,
    IActivityLogRepository logs,
    IUnitOfWork uow,
    TimeProvider clock) : IProjectService
{
    public async Task<ProjectDto> CreateAsync(CreateProjectRequest request, int userId, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var project = new Project
        {
            Name = request.Name.Trim(),
            Description = request.Description?.Trim(),
            OwnerId = userId,
            CreatedAt = now
        };

        await projects.AddAsync(project, ct);
        await logs.AddAsync(new ActivityLog
        {
            Project = project,
            UserId = userId,
            Action = "ProjectCreated",
            Details = project.Name,
            CreatedAt = now
        }, ct);

        await uow.SaveChangesAsync(ct);

        return new ProjectDto(project.Id, project.Name, project.Description, project.OwnerId, project.CreatedAt, 0);
    }

    public Task<IReadOnlyList<ProjectDto>> GetAllAsync(CancellationToken ct = default) => projects.GetAllAsync(ct);
}
