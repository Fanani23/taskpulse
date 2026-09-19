using TaskPulse.Api.Data;
using TaskPulse.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace TaskPulse.Api.Repositories;

public sealed class PostgresTaskRepository(TasksDbContext db) : ITaskRepository
{
    public async Task<TaskItem?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await db.Tasks.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        return entity?.ToItem();
    }

    public async Task<(IReadOnlyList<TaskItem> Items, int Total)> ListAsync(
        TaskItemStatus? status, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        IQueryable<TaskEntity> query = db.Tasks.AsNoTracking();
        if (status is { } wanted)
        {
            query = query.Where(t => t.Status == wanted);
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

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var deleted = await db.Tasks.Where(t => t.Id == id).ExecuteDeleteAsync(cancellationToken);
        return deleted > 0;
    }

    public Task<int> CountAsync(CancellationToken cancellationToken = default)
        => db.Tasks.CountAsync(cancellationToken);
}
