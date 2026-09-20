using TaskPulse.Api.Models;

namespace TaskPulse.Api.Services;

public interface ITaskService
{
    Task<PagedResponse<TaskItem>> ListAsync(TaskListQuery query, CancellationToken cancellationToken = default);

    Task<TaskItem?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<TaskStats> GetStatsAsync(TaskStatsQuery query, CancellationToken cancellationToken = default);

    Task<TaskItem> CreateAsync(CreateTaskRequest request, CancellationToken cancellationToken = default);

    Task<TaskItem?> UpdateAsync(Guid id, UpdateTaskRequest request, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
