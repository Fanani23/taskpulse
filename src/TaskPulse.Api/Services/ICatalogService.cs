using TaskPulse.Api.Models;

namespace TaskPulse.Api.Services;

public enum CatalogWriteOutcome
{
    Ok,
    NotFound,
    Conflict,
    InvalidCode,
    SchemaViolation,
    VersionMismatch,
}

public sealed record CatalogWriteResult(CatalogWriteOutcome Outcome, CatalogItem? Item, IReadOnlyList<string>? Errors = null)
{
    public static CatalogWriteResult Ok(CatalogItem item) => new(CatalogWriteOutcome.Ok, item);

    public static readonly CatalogWriteResult NotFound = new(CatalogWriteOutcome.NotFound, null);

    public static readonly CatalogWriteResult Conflict = new(CatalogWriteOutcome.Conflict, null);

    public static readonly CatalogWriteResult InvalidCode = new(CatalogWriteOutcome.InvalidCode, null);

    public static readonly CatalogWriteResult VersionMismatch = new(CatalogWriteOutcome.VersionMismatch, null);

    public static CatalogWriteResult Invalid(IReadOnlyList<string> errors) => new(CatalogWriteOutcome.SchemaViolation, null, errors);
}

public interface ICatalogService
{
    Task<IReadOnlyList<CatalogKindSummary>> ListKindsAsync(CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<CatalogItem> Items, int Total, int Page, int PageSize)> ListAsync(string kind, CatalogListQuery query, CancellationToken cancellationToken = default);

    Task<CatalogItem?> GetAsync(string kind, string code, CancellationToken cancellationToken = default);

    Task<CatalogWriteResult> CreateAsync(string kind, CreateCatalogItemRequest request, CancellationToken cancellationToken = default);

    Task<CatalogWriteResult> UpdateAsync(string kind, string code, UpdateCatalogItemRequest request, uint? expectedVersion, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(string kind, string code, CancellationToken cancellationToken = default);

    Task<CatalogSchema?> GetSchemaAsync(string kind, CancellationToken cancellationToken = default);

    Task<(bool Ok, IReadOnlyList<string> Errors, CatalogSchema? Schema)> PutSchemaAsync(string kind, PutCatalogSchemaRequest request, CancellationToken cancellationToken = default);

    Task<bool> DeleteSchemaAsync(string kind, CancellationToken cancellationToken = default);
}
