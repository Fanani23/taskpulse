using Microsoft.EntityFrameworkCore;
using TaskPulse.Api.Data;
using TaskPulse.Api.Models;

namespace TaskPulse.Api.Repositories;

public sealed class PostgresTaskRepository(TasksDbContext db) : ITaskRepository
{
    private const int RecentCount = 5;

    public async Task<TaskItem?> GetAsync(Guid id, bool includeDeleted = false, CancellationToken cancellationToken = default)
    {
        var entity = await Live(includeDeleted).FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        return entity?.ToItem();
    }

    public async Task<(IReadOnlyList<TaskItem> Items, int Total)> ListAsync(
        TaskItemStatus? status, string? search, bool includeDeleted, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = Live(includeDeleted);
        if (status is { } wanted)
        {
            query = query.Where(t => t.Status == wanted);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{LikePatterns.Escape(search.Trim())}%";
            query = query.Where(t => EF.Functions.ILike(t.Title, pattern, LikePatterns.EscapeCharacter)
                                  || (t.Description != null && EF.Functions.ILike(t.Description, pattern, LikePatterns.EscapeCharacter)));
        }

        var total = await query.CountAsync(cancellationToken);

        var entities = await query
            .OrderBy(t => t.CreatedAtUtc)
            .ThenBy(t => t.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (entities.Select(e => e.ToItem()).ToList(), total);
    }

    public async Task AddAsync(TaskItem item, CancellationToken cancellationToken = default)
    {
        db.Tasks.Add(TaskEntity.From(item));
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> UpdateAsync(TaskItem item, CancellationToken cancellationToken = default)
    {
        var entity = await db.Tasks.FindAsync([item.Id], cancellationToken);
        if (entity is null)
        {
            return false;
        }

        entity.Apply(item);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            return false;
        }
    }

    public async Task<bool> PurgeAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var deleted = await db.Tasks.Where(t => t.Id == id).ExecuteDeleteAsync(cancellationToken);
        return deleted > 0;
    }

    public Task<int> CountAsync(CancellationToken cancellationToken = default)
        => Live(false).CountAsync(cancellationToken);

    public async Task<TaskStats> GetStatsAsync(DateTimeOffset now, int days, CancellationToken cancellationToken = default)
    {
        var today = now.UtcDateTime.Date;
        var windowStart = today.AddDays(1 - days);
        var weekStart = today.AddDays(-6);
        var previousWeekStart = weekStart.AddDays(-7);
        var doneWindowStart = windowStart < previousWeekStart ? windowStart : previousWeekStart;

        var byStatus = await Live(false)
            .GroupBy(t => t.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Status, x => x.Count, cancellationToken);
        foreach (var status in Enum.GetValues<TaskItemStatus>())
        {
            byStatus.TryAdd(status, 0);
        }

        var total = byStatus.Values.Sum();
        var done = byStatus[TaskItemStatus.Done];

        var createdPerDay = await Live(false)
            .Where(t => t.CreatedAtUtc >= windowStart)
            .GroupBy(t => t.CreatedAtUtc.Date)
            .Select(g => new { Day = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Day, x => x.Count, cancellationToken);

        var donePerDay = await Live(false)
            .Where(t => t.Status == TaskItemStatus.Done && t.UpdatedAtUtc >= doneWindowStart)
            .GroupBy(t => t.UpdatedAtUtc.Date)
            .Select(g => new { Day = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Day, x => x.Count, cancellationToken);

        var daily = Enumerable.Range(0, days)
            .Select(offset => windowStart.AddDays(offset))
            .Select(day => new DailyTaskCount(DateOnly.FromDateTime(day), createdPerDay.GetValueOrDefault(day), donePerDay.GetValueOrDefault(day)))
            .ToList();

        var oldestOpen = await Live(false)
            .Where(t => t.Status != TaskItemStatus.Done)
            .OrderBy(t => t.CreatedAtUtc)
            .ThenBy(t => t.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var recent = await Live(false)
            .OrderByDescending(t => t.UpdatedAtUtc)
            .ThenBy(t => t.Id)
            .Take(RecentCount)
            .ToListAsync(cancellationToken);

        return new TaskStats(
            total,
            byStatus,
            total == 0 ? 0 : Math.Round((double)done / total, 3),
            createdPerDay.GetValueOrDefault(today),
            donePerDay.Where(kv => kv.Key >= weekStart).Sum(kv => kv.Value),
            donePerDay.Where(kv => kv.Key >= previousWeekStart && kv.Key < weekStart).Sum(kv => kv.Value),
            oldestOpen is null ? null : Summarize(oldestOpen),
            recent.Select(Summarize).ToList(),
            daily);
    }

    private IQueryable<TaskEntity> Live(bool includeDeleted)
        => includeDeleted ? db.Tasks.AsNoTracking() : db.Tasks.AsNoTracking().Where(t => t.DeletedAtUtc == null);

    private static TaskSummary Summarize(TaskEntity entity)
    {
        var item = entity.ToItem();
        return new TaskSummary(item.Id, item.Title, item.Status, item.CreatedAt, item.UpdatedAt);
    }
}
