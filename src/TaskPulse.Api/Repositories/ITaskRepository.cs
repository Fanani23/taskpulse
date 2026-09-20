using TaskPulse.Api.Models;

namespace TaskPulse.Api.Repositories;

public interface ITaskRepository
{
    Task<TaskItem?> GetAsync(Guid id, bool includeDeleted = false, CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<TaskItem> Items, int Total)> ListAsync(
        TaskItemStatus? status, string? search, bool includeDeleted, int page, int pageSize, CancellationToken cancellationToken = default);

    Task AddAsync(TaskItem item, CancellationToken cancellationToken = default);

    Task<bool> UpdateAsync(TaskItem item, CancellationToken cancellationToken = default);

    Task<bool> PurgeAsync(Guid id, CancellationToken cancellationToken = default);

    Task<int> CountAsync(CancellationToken cancellationToken = default);

    Task<TaskStats> GetStatsAsync(DateTimeOffset now, int days, CancellationToken cancellationToken = default);
}
