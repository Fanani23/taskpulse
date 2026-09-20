using Microsoft.Extensions.Options;
using TaskPulse.Api.Infrastructure;
using TaskPulse.Api.Models;
using TaskPulse.Api.Repositories;

namespace TaskPulse.Api.Services;

public sealed class TaskService(
    ITaskRepository repository,
    IAuditService audit,
    ICurrentUser user,
    IOptions<ApiOptions> options,
    TimeProvider clock,
    ILogger<TaskService> logger) : ITaskService
{
    private const string Resource = "task";

    public async Task<PagedResponse<TaskItem>> ListAsync(TaskListQuery query, CancellationToken cancellationToken = default)
    {
        var page = Math.Max(query.Page ?? 1, 1);
        var pageSize = Math.Clamp(query.PageSize ?? options.Value.DefaultPageSize, 1, options.Value.MaxPageSize);

        var assignee = string.Equals(query.Assignee, "me", StringComparison.OrdinalIgnoreCase) ? user.Sub : query.Assignee;
        var due = query.Due?.ToLowerInvariant() switch
        {
            "overdue" => TaskDueFilter.Overdue,
            "today" => TaskDueFilter.Today,
            "week" => TaskDueFilter.Week,
            "none" => TaskDueFilter.None,
            _ => TaskDueFilter.Any,
        };
        var filter = new TaskFilter(query.Priority, assignee, NormalizeLabel(query.Label), due, clock.GetUtcNow());
        var (items, total) = await repository.ListAsync(query.Status, query.Q, query.IncludeDeleted, page, pageSize, filter, cancellationToken);
        return new PagedResponse<TaskItem>(items, page, pageSize, total);
    }

    public Task<TaskItem?> GetAsync(Guid id, bool includeDeleted = false, CancellationToken cancellationToken = default)
        => repository.GetAsync(id, includeDeleted, cancellationToken);

    public Task<TaskStats> GetStatsAsync(TaskStatsQuery query, CancellationToken cancellationToken = default)
        => repository.GetStatsAsync(clock.GetUtcNow(), query.Days ?? TaskLimits.StatsDefaultDays, cancellationToken);

    public async Task<TaskItem> CreateAsync(CreateTaskRequest request, CancellationToken cancellationToken = default)
    {
        var now = Now();
        var item = new TaskItem(
            Guid.NewGuid(),
            request.Title!.Trim(),
            NormalizeDescription(request.Description),
            TaskItemStatus.Todo,
            now,
            now,
            CreatedBy: user.Actor,
            UpdatedBy: user.Actor,
            Priority: request.Priority ?? TaskPriority.Normal,
            DueAt: request.DueAt,
            AssigneeId: NormalizeAssignee(request.AssigneeId),
            AssigneeName: NormalizeAssignee(request.AssigneeName),
            Labels: NormalizeLabels(request.Labels));

        await repository.AddAsync(item, cancellationToken);
        logger.LogInformation("Task {TaskId} created by {Actor}", item.Id, user.Actor);
        await audit.RecordAsync("create", Resource, item.Id.ToString(), item.Title, cancellationToken: cancellationToken);
        return await repository.GetAsync(item.Id, false, cancellationToken) ?? item;
    }

    public async Task<WriteResult<TaskItem>> UpdateAsync(Guid id, UpdateTaskRequest request, uint? expectedVersion, CancellationToken cancellationToken = default)
    {
        var existing = await repository.GetAsync(id, includeDeleted: false, cancellationToken);
        if (existing is null)
        {
            return WriteResult<TaskItem>.NotFound;
        }

        if (expectedVersion is { } expected && expected != existing.Version)
        {
            return WriteResult<TaskItem>.VersionMismatch;
        }

        var updated = existing with
        {
            Title = request.Title!.Trim(),
            Description = NormalizeDescription(request.Description),
            Status = request.Status!.Value,
            UpdatedAt = Now(),
            UpdatedBy = user.Actor,
            Priority = request.Priority ?? existing.Priority,
            DueAt = request.DueAt,
            AssigneeId = NormalizeAssignee(request.AssigneeId),
            AssigneeName = NormalizeAssignee(request.AssigneeName),
            Labels = request.Labels is null ? existing.Labels : NormalizeLabels(request.Labels),
        };

        if (!await repository.UpdateAsync(updated, cancellationToken))
        {
            return WriteResult<TaskItem>.NotFound;
        }

        var action = updated.Status != existing.Status ? "move" : "update";
        await audit.RecordAsync(action, Resource, id.ToString(), updated.Status != existing.Status ? $"{updated.Title} → {updated.Status}" : updated.Title, cancellationToken: cancellationToken);
        return WriteResult<TaskItem>.Ok(await repository.GetAsync(id, false, cancellationToken) ?? updated);
    }

    public async Task<WriteResult<TaskItem>> DeleteAsync(Guid id, bool permanent, CancellationToken cancellationToken = default)
    {
        if (permanent)
        {
            if (!user.IsAdmin)
            {
                return WriteResult<TaskItem>.Forbidden;
            }

            var existing = await repository.GetAsync(id, includeDeleted: true, cancellationToken);
            if (existing is null || !await repository.PurgeAsync(id, cancellationToken))
            {
                return WriteResult<TaskItem>.NotFound;
            }

            logger.LogInformation("Task {TaskId} purged by {Actor}", id, user.Actor);
            await audit.RecordAsync("purge", Resource, id.ToString(), existing.Title, cancellationToken: cancellationToken);
            return WriteResult<TaskItem>.Ok(existing);
        }

        var live = await repository.GetAsync(id, includeDeleted: false, cancellationToken);
        if (live is null)
        {
            return WriteResult<TaskItem>.NotFound;
        }

        var deleted = live with { DeletedAt = Now(), UpdatedAt = Now(), UpdatedBy = user.Actor };
        if (!await repository.UpdateAsync(deleted, cancellationToken))
        {
            return WriteResult<TaskItem>.NotFound;
        }

        logger.LogInformation("Task {TaskId} deleted by {Actor}", id, user.Actor);
        await audit.RecordAsync("delete", Resource, id.ToString(), live.Title, cancellationToken: cancellationToken);
        return WriteResult<TaskItem>.Ok(deleted);
    }

    public async Task<WriteResult<TaskItem>> RestoreAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var existing = await repository.GetAsync(id, includeDeleted: true, cancellationToken);
        if (existing is null)
        {
            return WriteResult<TaskItem>.NotFound;
        }

        if (existing.DeletedAt is null)
        {
            return WriteResult<TaskItem>.Ok(existing);
        }

        var restored = existing with { DeletedAt = null, UpdatedAt = Now(), UpdatedBy = user.Actor };
        if (!await repository.UpdateAsync(restored, cancellationToken))
        {
            return WriteResult<TaskItem>.NotFound;
        }

        await audit.RecordAsync("restore", Resource, id.ToString(), restored.Title, cancellationToken: cancellationToken);
        return WriteResult<TaskItem>.Ok(await repository.GetAsync(id, false, cancellationToken) ?? restored);
    }

    private DateTimeOffset Now() => Timestamps.ToMicroseconds(clock.GetUtcNow());

    private static string? NormalizeDescription(string? description)
        => string.IsNullOrWhiteSpace(description) ? null : description.Trim();

    private static string? NormalizeAssignee(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? NormalizeLabel(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();

    // Labels are lower-cased, trimmed, de-duplicated, capped in count and length; an empty one is dropped.
    public static IReadOnlyList<string> NormalizeLabels(IEnumerable<string>? labels)
        => labels is null
            ? []
            : labels.Select(l => (l ?? "").Trim().ToLowerInvariant())
                    .Where(l => l.Length > 0)
                    .Select(l => l.Length > TaskLimits.LabelMaxLength ? l[..TaskLimits.LabelMaxLength] : l)
                    .Distinct(StringComparer.Ordinal)
                    .Take(TaskLimits.LabelsMax)
                    .ToList();
}
