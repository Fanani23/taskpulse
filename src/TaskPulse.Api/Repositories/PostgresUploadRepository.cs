using Microsoft.EntityFrameworkCore;
using TaskPulse.Api.Data;
using TaskPulse.Api.Models;

namespace TaskPulse.Api.Repositories;

public sealed class PostgresUploadRepository(TasksDbContext db) : IUploadRepository
{
    public async Task<IReadOnlyList<UploadItem>> ListAsync(string? source, int limit, CancellationToken cancellationToken = default)
    {
        IQueryable<UploadEntity> query = db.Uploads.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(source))
        {
            query = query.Where(u => u.Source == source);
        }

        var entities = await query
            .OrderByDescending(u => u.CreatedAtUtc)
            .ThenBy(u => u.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);
        return entities.Select(e => e.ToItem()).ToList();
    }

    public async Task<UploadItem?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await db.Uploads.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
        return entity?.ToItem();
    }

    public async Task AddAsync(UploadItem item, CancellationToken cancellationToken = default)
    {
        db.Uploads.Add(UploadEntity.From(item));
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
        => await db.Uploads.Where(u => u.Id == id).ExecuteDeleteAsync(cancellationToken) > 0;
}
