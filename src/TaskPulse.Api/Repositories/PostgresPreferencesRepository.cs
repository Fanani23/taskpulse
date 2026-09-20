using Microsoft.EntityFrameworkCore;
using TaskPulse.Api.Data;
using TaskPulse.Api.Models;

namespace TaskPulse.Api.Repositories;

public sealed class PostgresPreferencesRepository(TasksDbContext db) : IPreferencesRepository
{
    public async Task<Preferences?> GetAsync(string userId, CancellationToken cancellationToken = default)
    {
        var entity = await db.Preferences.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);
        return entity?.ToItem();
    }

    public async Task UpsertAsync(Preferences item, CancellationToken cancellationToken = default)
    {
        var entity = await db.Preferences.FindAsync([item.UserId], cancellationToken);
        if (entity is null)
        {
            db.Preferences.Add(PreferenceEntity.From(item));
        }
        else
        {
            entity.Apply(item);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> DeleteAsync(string userId, CancellationToken cancellationToken = default)
        => await db.Preferences.Where(p => p.UserId == userId).ExecuteDeleteAsync(cancellationToken) > 0;
}
