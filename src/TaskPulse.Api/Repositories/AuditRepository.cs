using Microsoft.EntityFrameworkCore;
using TaskPulse.Api.Data;
using TaskPulse.Api.Models;

namespace TaskPulse.Api.Repositories;

public interface IAuditRepository
{
    Task AddAsync(AuditEntity entry, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AuditEntry>> ListAsync(string? resource, int limit, CancellationToken cancellationToken = default);
}

public sealed class PostgresAuditRepository(TasksDbContext db) : IAuditRepository
{
    public async Task AddAsync(AuditEntity entry, CancellationToken cancellationToken = default)
    {
        db.Audit.Add(entry);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AuditEntry>> ListAsync(string? resource, int limit, CancellationToken cancellationToken = default)
    {
        IQueryable<AuditEntity> query = db.Audit.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(resource))
        {
            query = query.Where(a => a.Resource == resource);
        }

        var rows = await query.OrderByDescending(a => a.Id).Take(limit).ToListAsync(cancellationToken);
        return rows.Select(r => r.ToItem()).ToList();
    }
}
