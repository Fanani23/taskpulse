using TaskPulse.Api.Infrastructure;
using TaskPulse.Api.Models;
using TaskPulse.Api.Repositories;
using Microsoft.Extensions.Options;

namespace TaskPulse.Api.Services;

public sealed class TaskService(
    ITaskRepository repository,
    IOptions<ApiOptions> options,
    TimeProvider clock,
    ILogger<TaskService> logger) : ITaskService
{
    public async Task<PagedResponse<TaskItem>> ListAsync(TaskListQuery query, CancellationToken cancellationToken = default)
    {
        var page = Math.Max(query.Page ?? 1, 1);
        var pageSize = Math.Clamp(query.PageSize ?? options.Value.DefaultPageSize, 1, options.Value.MaxPageSize);

        var (items, total) = await repository.ListAsync(query.Status, page, pageSize, cancellationToken);
        return new PagedResponse<TaskItem>(items, page, pageSize, total);
    }

    public Task<TaskItem?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => repository.GetAsync(id, cancellationToken);

    public async Task<TaskItem> CreateAsync(CreateTaskRequest request, CancellationToken cancellationToken = default)
    {
        var now = Now();
        var item = new TaskItem(
            Guid.NewGuid(),
            request.Title!.Trim(),
            NormalizeDescription(request.Description),
            TaskItemStatus.Todo,
            now,
            now);

        await repository.AddAsync(item, cancellationToken);
        logger.LogInformation("Task {TaskId} created", item.Id);
        return item;
    }

    public async Task<TaskItem?> UpdateAsync(Guid id, UpdateTaskRequest request, CancellationToken cancellationToken = default)
    {
        var existing = await repository.GetAsync(id, cancellationToken);
        if (existing is null)
        {
            return null;
        }

        var updated = existing with
        {
            Title = request.Title!.Trim(),
            Description = NormalizeDescription(request.Description),
            Status = request.Status!.Value,
            UpdatedAt = Now(),
        };

        return await repository.UpdateAsync(updated, cancellationToken) ? updated : null;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (!await repository.DeleteAsync(id, cancellationToken))
        {
            return false;
        }

        logger.LogInformation("Task {TaskId} deleted", id);
        return true;
    }

    private DateTimeOffset Now() => Timestamps.ToMicroseconds(clock.GetUtcNow());

    private static string? NormalizeDescription(string? description)
        => string.IsNullOrWhiteSpace(description) ? null : description.Trim();
}
