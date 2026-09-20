using TaskPulse.Api.Models;

namespace TaskPulse.Api.Repositories;

public interface ICatalogRepository
{
    Task<IReadOnlyList<CatalogKindSummary>> ListKindsAsync(CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<CatalogItem> Items, int Total)> ListAsync(string kind, string? parent, string? search, int page, int pageSize, CancellationToken cancellationToken = default);

    Task<CatalogItem?> GetAsync(string kind, string code, CancellationToken cancellationToken = default);

    Task<bool> AddAsync(CatalogItem item, CancellationToken cancellationToken = default);

    Task<bool> UpdateAsync(CatalogItem item, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(string kind, string code, DateTimeOffset now, CancellationToken cancellationToken = default);

    Task<CatalogSchema?> GetSchemaAsync(string kind, CancellationToken cancellationToken = default);

    Task UpsertSchemaAsync(CatalogSchema schema, CancellationToken cancellationToken = default);

    Task<bool> DeleteSchemaAsync(string kind, CancellationToken cancellationToken = default);
}
