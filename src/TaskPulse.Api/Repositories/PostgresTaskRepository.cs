using Microsoft.EntityFrameworkCore;
using TaskPulse.Api.Data;
using TaskPulse.Api.Infrastructure;
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

    public async Task<(IReadOnlyList<TaskItem> Items, int Total, TaskCursor? Next)> ListAsync(
        TaskItemStatus? status, string? search, bool includeDeleted, int page, int pageSize, TaskFilter? filter = null, TaskCursor? after = null, CancellationToken cancellationToken = default)
    {
        var query = Live(includeDeleted);
        if (status is { } wanted)
        {
            query = query.Where(t => t.Status == wanted);
        }

        if (filter is not null)
        {
            if (filter.Priority is { } priority)
            {
                query = query.Where(t => t.Priority == priority);
            }

            if (!string.IsNullOrEmpty(filter.AssigneeId))
            {
                query = query.Where(t => t.AssigneeId == filter.AssigneeId);
            }

            if (!string.IsNullOrEmpty(filter.Label))
            {
                var label = filter.Label;
                query = query.Where(t => t.Labels.Contains(label));
            }

            var now = filter.Now.UtcDateTime;
            query = filter.Due switch
            {
                TaskDueFilter.Overdue => query.Where(t => t.DueAtUtc != null && t.DueAtUtc < now && t.Status != TaskItemStatus.Done),
                TaskDueFilter.Today => query.Where(t => t.DueAtUtc != null && t.DueAtUtc >= now.Date && t.DueAtUtc < now.Date.AddDays(1)),
                TaskDueFilter.Week => query.Where(t => t.DueAtUtc != null && t.DueAtUtc >= now && t.DueAtUtc < now.AddDays(7)),
                TaskDueFilter.None => query.Where(t => t.DueAtUtc == null),
                _ => query,
            };
        }

        // Full text: every word of the search is a stemmed prefix term ("post" -> post:*), all required. A search with no
        // word characters (e.g. "%") matches nothing rather than everything.
        var tsQuery = FullText.ToPrefixQuery(search);
        if (search is not null && search.Trim().Length > 0 && tsQuery is null)
        {
            return ([], 0, null);
        }

        if (tsQuery is not null)
        {
            query = query.Where(t => t.SearchVector!.Matches(EF.Functions.ToTsQuery("english", tsQuery)));
        }

        var total = await query.CountAsync(cancellationToken);

        // Search results by relevance; otherwise open tasks with a due date first (soonest first), then by creation.
        // Both orders end with the id, so the sort key is unique and a cursor (the last row's key) resumes exactly
        // after it: `WHERE (key) > (cursor)` instead of OFFSET.
        List<(TaskEntity Entity, float? Rank)> rows;
        if (tsQuery is not null)
        {
            var ranked = query.Select(t => new { Entity = t, Rank = t.SearchVector!.Rank(EF.Functions.ToTsQuery("english", tsQuery)) });
            if (after is { Rank: { } afterRank })
            {
                ranked = ranked.Where(r => EF.Functions.GreaterThan(ValueTuple.Create(-r.Rank, r.Entity.CreatedAtUtc, r.Entity.Id), ValueTuple.Create(-afterRank, after.CreatedAt, after.Id)));
            }

            var page1 = await ranked
                .OrderByDescending(r => r.Rank).ThenBy(r => r.Entity.CreatedAtUtc).ThenBy(r => r.Entity.Id)
                .Skip(after is null ? (page - 1) * pageSize : 0)
                .Take(pageSize + 1) // one extra row tells whether a next page exists
                .ToListAsync(cancellationToken);
            rows = page1.Select(r => (r.Entity, (float?)r.Rank)).ToList();
        }
        else
        {
            if (after is not null)
            {
                var afterDue = after.DueAt ?? DateTime.MaxValue;
                query = query.Where(t => EF.Functions.GreaterThan(
                    ValueTuple.Create(t.Status == TaskItemStatus.Done, t.DueAtUtc == null, t.DueAtUtc ?? DateTime.MaxValue, t.CreatedAtUtc, t.Id),
                    ValueTuple.Create(after.Done, after.DueNull, afterDue, after.CreatedAt, after.Id)));
            }

            var page1 = await query
                .OrderBy(t => t.Status == TaskItemStatus.Done)
                .ThenBy(t => t.DueAtUtc == null)
                .ThenBy(t => t.DueAtUtc)
                .ThenBy(t => t.CreatedAtUtc)
                .ThenBy(t => t.Id)
                .Skip(after is null ? (page - 1) * pageSize : 0)
                .Take(pageSize + 1) // one extra row tells whether a next page exists
                .ToListAsync(cancellationToken);
            rows = page1.Select(t => (t, (float?)null)).ToList();
        }

        TaskCursor? next = null;
        if (rows.Count > pageSize)
        {
            rows.RemoveRange(pageSize, rows.Count - pageSize);
            var (last, rank) = rows[^1];
            next = new TaskCursor(last.Status == TaskItemStatus.Done, last.DueAtUtc == null, last.DueAtUtc, last.CreatedAtUtc, last.Id, rank);
        }

        return (rows.Select(r => r.Entity.ToItem()).ToList(), total, next);
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

        var nowUtc = now.UtcDateTime;
        var overdue = await Live(false).CountAsync(t => t.Status != TaskItemStatus.Done && t.DueAtUtc != null && t.DueAtUtc < nowUtc, cancellationToken);
        var dueThisWeek = await Live(false).CountAsync(t => t.Status != TaskItemStatus.Done && t.DueAtUtc != null && t.DueAtUtc >= nowUtc && t.DueAtUtc < nowUtc.AddDays(7), cancellationToken);

        return new TaskStats(
            total,
            byStatus,
            total == 0 ? 0 : Math.Round((double)done / total, 3),
            createdPerDay.GetValueOrDefault(today),
            donePerDay.Where(kv => kv.Key >= weekStart).Sum(kv => kv.Value),
            donePerDay.Where(kv => kv.Key >= previousWeekStart && kv.Key < weekStart).Sum(kv => kv.Value),
            overdue,
            dueThisWeek,
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
