using TaskPulse.Api.Models;
using TaskPulse.Api.Repositories;

namespace TaskPulse.Api.Services;

public interface IPreferencesService
{
    Task<Preferences?> GetAsync(string userId, CancellationToken cancellationToken = default);

    Task<Preferences> UpsertAsync(string userId, UpsertPreferencesRequest request, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(string userId, CancellationToken cancellationToken = default);
}

public sealed class PreferencesService(IPreferencesRepository repository, TimeProvider clock) : IPreferencesService
{
    public Task<Preferences?> GetAsync(string userId, CancellationToken cancellationToken = default)
        => repository.GetAsync(userId, cancellationToken);

    public async Task<Preferences> UpsertAsync(string userId, UpsertPreferencesRequest request, CancellationToken cancellationToken = default)
    {
        var item = new Preferences(
            userId,
            request.Theme!,
            string.IsNullOrWhiteSpace(request.Nickname) ? null : request.Nickname.Trim(),
            Timestamps.ToMicroseconds(clock.GetUtcNow()));
        await repository.UpsertAsync(item, cancellationToken);
        return item;
    }

    public Task<bool> DeleteAsync(string userId, CancellationToken cancellationToken = default)
        => repository.DeleteAsync(userId, cancellationToken);
}
