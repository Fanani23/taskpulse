using TaskPulse.Api.Models;

namespace TaskPulse.Api.Repositories;

public interface IPreferencesRepository
{
    Task<Preferences?> GetAsync(string userId, CancellationToken cancellationToken = default);

    Task UpsertAsync(Preferences item, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(string userId, CancellationToken cancellationToken = default);
}
