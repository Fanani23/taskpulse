using TaskPulse.Api.Data;
using TaskPulse.Api.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace TaskPulse.Api.Repositories;

public sealed class PostgresCatalogRepository(TasksDbContext db) : ICatalogRepository
{
    public async Task<IReadOnlyList<CatalogKindSummary>> ListKindsAsync(CancellationToken cancellationToken = default)
        => (await db.Catalog.AsNoTracking()
            .GroupBy(c => c.Kind)
            .Select(g => new { Kind = g.Key, Count = g.Count() })
            .OrderBy(k => k.Kind)
            .ToListAsync(cancellationToken))
            .Select(k => new CatalogKindSummary(k.Kind, k.Count))
            .ToList();

    public async Task<IReadOnlyList<CatalogItem>> ListAsync(string kind, string? parent, string? search, int limit, CancellationToken cancellationToken = default)
    {
        IQueryable<CatalogEntity> query = db.Catalog.AsNoTracking().Where(c => c.Kind == kind);
        if (!string.IsNullOrWhiteSpace(parent))
        {
            query = query.Where(c => c.Parents.Contains(parent));
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{LikePatterns.Escape(search.Trim())}%";
            query = query.Where(c => EF.Functions.ILike(c.Label, pattern, LikePatterns.EscapeCharacter)
                                  || EF.Functions.ILike(c.Code, pattern, LikePatterns.EscapeCharacter));
        }

        var entities = await query
            .OrderBy(c => c.Sort)
            .ThenBy(c => c.Label)
            .ThenBy(c => c.Code)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return entities.Select(e => e.ToItem()).ToList();
    }

    public async Task<CatalogItem?> GetAsync(string kind, string code, CancellationToken cancellationToken = default)
    {
        var entity = await db.Catalog.AsNoTracking().FirstOrDefaultAsync(c => c.Kind == kind && c.Code == code, cancellationToken);
        return entity?.ToItem();
    }

    public async Task<bool> AddAsync(CatalogItem item, CancellationToken cancellationToken = default)
    {
        var entity = CatalogEntity.From(item);
        db.Catalog.Add(entity);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.Entry(entity).State = EntityState.Detached;
            return false;
        }
    }

    public async Task<bool> UpdateAsync(CatalogItem item, CancellationToken cancellationToken = default)
    {
        var entity = await db.Catalog.FirstOrDefaultAsync(c => c.Kind == item.Kind && c.Code == item.Code, cancellationToken);
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

    public Task<bool> DeleteAsync(string kind, string code, DateTimeOffset now, CancellationToken cancellationToken = default)
        => db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var deleted = await db.Catalog.Where(c => c.Kind == kind && c.Code == code).ExecuteDeleteAsync(cancellationToken);
            if (deleted == 0)
            {
                return false;
            }

            var stamp = now.UtcDateTime;
            await db.Database.ExecuteSqlAsync(
                $"""UPDATE catalog SET "Parents" = array_remove("Parents", {code}), "UpdatedAtUtc" = {stamp} WHERE {code} = ANY("Parents")""",
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        });
}
