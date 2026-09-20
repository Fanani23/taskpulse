using TaskPulse.Api.Models;

namespace TaskPulse.Api.Repositories;

public interface IUploadRepository
{
    Task<IReadOnlyList<UploadItem>> ListAsync(string? source, int limit, CancellationToken cancellationToken = default);

    Task<UploadItem?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task AddAsync(UploadItem item, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
