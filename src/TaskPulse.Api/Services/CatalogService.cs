using System.Text.Json;
using Json.Schema;
using TaskPulse.Api.Infrastructure;
using TaskPulse.Api.Models;
using TaskPulse.Api.Repositories;

namespace TaskPulse.Api.Services;

public sealed class CatalogService(
    ICatalogRepository repository,
    IAuditService audit,
    ICurrentUser user,
    TimeProvider clock,
    ILogger<CatalogService> logger) : ICatalogService
{
    public const string Resource = "catalog";

    public Task<IReadOnlyList<CatalogKindSummary>> ListKindsAsync(CancellationToken cancellationToken = default)
        => repository.ListKindsAsync(cancellationToken);

    public async Task<(IReadOnlyList<CatalogItem> Items, int Total, int Page, int PageSize)> ListAsync(string kind, CatalogListQuery query, CancellationToken cancellationToken = default)
    {
        var page = Math.Max(query.Page ?? 1, 1);
        var pageSize = Math.Clamp(query.PageSize ?? CatalogLimits.ListMax, 1, CatalogLimits.ListMax);
        var (items, total) = await repository.ListAsync(kind, query.Parent, query.Q, page, pageSize, cancellationToken);
        return (items, total, page, pageSize);
    }

    public Task<CatalogItem?> GetAsync(string kind, string code, CancellationToken cancellationToken = default)
        => repository.GetAsync(kind, code, cancellationToken);

    public async Task<CatalogWriteResult> CreateAsync(string kind, CreateCatalogItemRequest request, CancellationToken cancellationToken = default)
    {
        var code = request.Code ?? CatalogLimits.Slug(request.Label!);
        if (!CatalogLimits.IsCode(code))
        {
            return CatalogWriteResult.InvalidCode;
        }

        var attributes = NormalizeAttributes(request.Attributes);
        var violations = await ValidateAgainstSchemaAsync(kind, attributes, cancellationToken);
        if (violations.Count > 0)
        {
            return CatalogWriteResult.Invalid(violations);
        }

        var now = Now();
        var item = new CatalogItem(
            Guid.NewGuid(),
            kind,
            code,
            request.Label!.Trim(),
            NormalizeParents(request.Parents),
            attributes,
            request.Sort ?? 0,
            now,
            now,
            CreatedBy: user.Actor,
            UpdatedBy: user.Actor);

        if (!await repository.AddAsync(item, cancellationToken))
        {
            return CatalogWriteResult.Conflict;
        }

        logger.LogInformation("Catalog {Kind}/{Code} created by {Actor}", kind, code, user.Actor);
        await audit.RecordAsync("create", Resource, code, item.Label, kind, cancellationToken: cancellationToken);
        return CatalogWriteResult.Ok(await repository.GetAsync(kind, code, cancellationToken) ?? item);
    }

    public async Task<CatalogWriteResult> UpdateAsync(string kind, string code, UpdateCatalogItemRequest request, uint? expectedVersion, CancellationToken cancellationToken = default)
    {
        var existing = await repository.GetAsync(kind, code, cancellationToken);
        if (existing is null)
        {
            return CatalogWriteResult.NotFound;
        }

        if (expectedVersion is { } expected && expected != existing.Version)
        {
            return CatalogWriteResult.VersionMismatch;
        }

        var attributes = request.Attributes is null ? existing.Attributes : NormalizeAttributes(request.Attributes);
        var violations = await ValidateAgainstSchemaAsync(kind, attributes, cancellationToken);
        if (violations.Count > 0)
        {
            return CatalogWriteResult.Invalid(violations);
        }

        var updated = existing with
        {
            Label = request.Label!.Trim(),
            Parents = request.Parents is null ? existing.Parents : NormalizeParents(request.Parents),
            Attributes = attributes,
            Sort = request.Sort ?? existing.Sort,
            UpdatedAt = Now(),
            UpdatedBy = user.Actor,
        };

        if (!await repository.UpdateAsync(updated, cancellationToken))
        {
            return CatalogWriteResult.NotFound;
        }

        await audit.RecordAsync("update", Resource, code, updated.Label, kind, Diff.Of(existing, updated), cancellationToken);
        return CatalogWriteResult.Ok(await repository.GetAsync(kind, code, cancellationToken) ?? updated);
    }

    public async Task<bool> DeleteAsync(string kind, string code, CancellationToken cancellationToken = default)
    {
        var existing = await repository.GetAsync(kind, code, cancellationToken);
        if (existing is null || !await repository.DeleteAsync(kind, code, Now(), cancellationToken))
        {
            return false;
        }

        logger.LogInformation("Catalog {Kind}/{Code} deleted by {Actor}", kind, code, user.Actor);
        await audit.RecordAsync("delete", Resource, code, existing.Label, kind, cancellationToken: cancellationToken);
        return true;
    }

    public Task<CatalogSchema?> GetSchemaAsync(string kind, CancellationToken cancellationToken = default)
        => repository.GetSchemaAsync(kind, cancellationToken);

    public async Task<(bool Ok, IReadOnlyList<string> Errors, CatalogSchema? Schema)> PutSchemaAsync(string kind, PutCatalogSchemaRequest request, CancellationToken cancellationToken = default)
    {
        JsonSchema schema;
        try
        {
            schema = JsonSchema.FromText(request.Schema!.Value.GetRawText());
        }
        catch (Exception e) when (e is JsonException or ArgumentException)
        {
            return (false, [$"Not a valid JSON Schema: {e.Message}"], null);
        }

        // Existing items must already satisfy the new schema, otherwise the kind becomes un-editable.
        var (items, _) = await repository.ListAsync(kind, null, null, 1, CatalogLimits.ListMax, cancellationToken);
        var failing = items
            .Select(i => (i.Code, Errors: Violations(schema, i.Attributes)))
            .Where(x => x.Errors.Count > 0)
            .Take(5)
            .Select(x => $"{x.Code}: {string.Join("; ", x.Errors)}")
            .ToList();
        if (failing.Count > 0)
        {
            return (false, failing, null);
        }

        var stored = new CatalogSchema(kind, request.Schema!.Value.Clone(), Now(), user.Actor);
        await repository.UpsertSchemaAsync(stored, cancellationToken);
        await audit.RecordAsync("schema", Resource, kind, $"schema for {kind} set", kind, cancellationToken: cancellationToken);
        return (true, [], stored);
    }

    public async Task<bool> DeleteSchemaAsync(string kind, CancellationToken cancellationToken = default)
    {
        if (!await repository.DeleteSchemaAsync(kind, cancellationToken))
        {
            return false;
        }

        await audit.RecordAsync("schema", Resource, kind, $"schema for {kind} removed", kind, cancellationToken: cancellationToken);
        return true;
    }

    private async Task<IReadOnlyList<string>> ValidateAgainstSchemaAsync(string kind, JsonElement? attributes, CancellationToken cancellationToken)
    {
        var stored = await repository.GetSchemaAsync(kind, cancellationToken);
        if (stored is null)
        {
            return [];
        }

        return Violations(JsonSchema.FromText(stored.Schema.GetRawText()), attributes);
    }

    private static IReadOnlyList<string> Violations(JsonSchema schema, JsonElement? attributes)
    {
        using var doc = JsonDocument.Parse(attributes is { ValueKind: JsonValueKind.Object } a ? a.GetRawText() : "{}");
        var result = schema.Evaluate(doc.RootElement, new EvaluationOptions { OutputFormat = OutputFormat.List });
        if (result.IsValid)
        {
            return [];
        }

        var errors = result.Details
            .Where(d => d.HasErrors)
            .SelectMany(d => d.Errors!.Select(e => $"{(d.InstanceLocation.Count == 0 ? "attributes" : d.InstanceLocation.ToString())}: {e.Value}"))
            .Distinct()
            .Take(10)
            .ToList();
        return errors.Count > 0 ? errors : ["attributes do not match the kind's schema"];
    }

    private DateTimeOffset Now() => Timestamps.ToMicroseconds(clock.GetUtcNow());

    private static IReadOnlyList<string> NormalizeParents(string[]? parents)
        => parents is null ? [] : parents.Select(p => p.Trim()).Where(p => p.Length > 0).Distinct(StringComparer.Ordinal).ToList();

    private static JsonElement? NormalizeAttributes(JsonElement? attributes)
        => attributes is { ValueKind: JsonValueKind.Object } value && value.EnumerateObject().Any() ? value.Clone() : null;
}
