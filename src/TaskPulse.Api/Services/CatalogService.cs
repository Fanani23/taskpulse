using System.Text.Json;
using TaskPulse.Api.Models;
using TaskPulse.Api.Repositories;

namespace TaskPulse.Api.Services;

public sealed class CatalogService(
    ICatalogRepository repository,
    TimeProvider clock,
    ILogger<CatalogService> logger) : ICatalogService
{
    public Task<IReadOnlyList<CatalogKindSummary>> ListKindsAsync(CancellationToken cancellationToken = default)
        => repository.ListKindsAsync(cancellationToken);

    public Task<IReadOnlyList<CatalogItem>> ListAsync(string kind, CatalogListQuery query, CancellationToken cancellationToken = default)
        => repository.ListAsync(kind, query.Parent, query.Q, CatalogLimits.ListMax, cancellationToken);

    public Task<CatalogItem?> GetAsync(string kind, string code, CancellationToken cancellationToken = default)
        => repository.GetAsync(kind, code, cancellationToken);

    public async Task<CatalogWriteResult> CreateAsync(string kind, CreateCatalogItemRequest request, CancellationToken cancellationToken = default)
    {
        var code = request.Code ?? CatalogLimits.Slug(request.Label!);
        if (!CatalogLimits.IsCode(code))
        {
            return CatalogWriteResult.InvalidCode;
        }

        var now = Now();
        var item = new CatalogItem(
            Guid.NewGuid(),
            kind,
            code,
            request.Label!.Trim(),
            NormalizeParents(request.Parents),
            NormalizeAttributes(request.Attributes),
            request.Sort ?? 0,
            now,
            now);

        if (!await repository.AddAsync(item, cancellationToken))
        {
            return CatalogWriteResult.Conflict;
        }

        logger.LogInformation("Catalog {Kind}/{Code} created", kind, code);
        return CatalogWriteResult.Ok(item);
    }

    public async Task<CatalogWriteResult> UpdateAsync(string kind, string code, UpdateCatalogItemRequest request, CancellationToken cancellationToken = default)
    {
        var existing = await repository.GetAsync(kind, code, cancellationToken);
        if (existing is null)
        {
            return CatalogWriteResult.NotFound;
        }

        var updated = existing with
        {
            Label = request.Label!.Trim(),
            Parents = request.Parents is null ? existing.Parents : NormalizeParents(request.Parents),
            Attributes = request.Attributes is null ? existing.Attributes : NormalizeAttributes(request.Attributes),
            Sort = request.Sort ?? existing.Sort,
            UpdatedAt = Now(),
        };

        return await repository.UpdateAsync(updated, cancellationToken) ? CatalogWriteResult.Ok(updated) : CatalogWriteResult.NotFound;
    }

    public async Task<bool> DeleteAsync(string kind, string code, CancellationToken cancellationToken = default)
    {
        if (!await repository.DeleteAsync(kind, code, Now(), cancellationToken))
        {
            return false;
        }

        logger.LogInformation("Catalog {Kind}/{Code} deleted", kind, code);
        return true;
    }

    private DateTimeOffset Now() => Timestamps.ToMicroseconds(clock.GetUtcNow());

    private static IReadOnlyList<string> NormalizeParents(string[]? parents)
        => parents is null ? [] : parents.Select(p => p.Trim()).Where(p => p.Length > 0).Distinct(StringComparer.Ordinal).ToList();

    private static JsonElement? NormalizeAttributes(JsonElement? attributes)
        => attributes is { ValueKind: JsonValueKind.Object } value && value.EnumerateObject().Any() ? value.Clone() : null;
}
