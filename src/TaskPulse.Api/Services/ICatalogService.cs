using TaskPulse.Api.Models;

namespace TaskPulse.Api.Services;

public enum CatalogWriteOutcome
{
    Ok,
    NotFound,
    Conflict,
    InvalidCode,
}

public sealed record CatalogWriteResult(CatalogWriteOutcome Outcome, CatalogItem? Item)
{
    public static CatalogWriteResult Ok(CatalogItem item) => new(CatalogWriteOutcome.Ok, item);

    public static readonly CatalogWriteResult NotFound = new(CatalogWriteOutcome.NotFound, null);

    public static readonly CatalogWriteResult Conflict = new(CatalogWriteOutcome.Conflict, null);

    public static readonly CatalogWriteResult InvalidCode = new(CatalogWriteOutcome.InvalidCode, null);
}

public interface ICatalogService
{
    Task<IReadOnlyList<CatalogKindSummary>> ListKindsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CatalogItem>> ListAsync(string kind, CatalogListQuery query, CancellationToken cancellationToken = default);

    Task<CatalogItem?> GetAsync(string kind, string code, CancellationToken cancellationToken = default);

    Task<CatalogWriteResult> CreateAsync(string kind, CreateCatalogItemRequest request, CancellationToken cancellationToken = default);

    Task<CatalogWriteResult> UpdateAsync(string kind, string code, UpdateCatalogItemRequest request, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(string kind, string code, CancellationToken cancellationToken = default);
}
