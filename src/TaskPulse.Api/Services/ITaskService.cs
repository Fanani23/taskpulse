using TaskPulse.Api.Models;

namespace TaskPulse.Api.Services;

public enum WriteOutcome
{
    Ok,
    NotFound,
    VersionMismatch,
    Forbidden,
}

public sealed record WriteResult<T>(WriteOutcome Outcome, T? Item)
{
    public static WriteResult<T> Ok(T item) => new(WriteOutcome.Ok, item);

    public static readonly WriteResult<T> NotFound = new(WriteOutcome.NotFound, default);

    public static readonly WriteResult<T> VersionMismatch = new(WriteOutcome.VersionMismatch, default);

    public static readonly WriteResult<T> Forbidden = new(WriteOutcome.Forbidden, default);
}

public interface ITaskService
{
    Task<PagedResponse<TaskItem>> ListAsync(TaskListQuery query, CancellationToken cancellationToken = default);

    Task<TaskItem?> GetAsync(Guid id, bool includeDeleted = false, CancellationToken cancellationToken = default);

    Task<TaskStats> GetStatsAsync(TaskStatsQuery query, CancellationToken cancellationToken = default);

    Task<TaskItem> CreateAsync(CreateTaskRequest request, CancellationToken cancellationToken = default);

    Task<WriteResult<TaskItem>> UpdateAsync(Guid id, UpdateTaskRequest request, uint? expectedVersion, CancellationToken cancellationToken = default);

    Task<WriteResult<TaskItem>> DeleteAsync(Guid id, bool permanent, CancellationToken cancellationToken = default);

    Task<WriteResult<TaskItem>> RestoreAsync(Guid id, CancellationToken cancellationToken = default);
}
