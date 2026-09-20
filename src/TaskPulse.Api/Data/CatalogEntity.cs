using System.Text.Json;
using TaskPulse.Api.Models;

namespace TaskPulse.Api.Data;

public sealed class CatalogEntity
{
    public Guid Id { get; set; }

    public required string Kind { get; set; }

    public required string Code { get; set; }

    public required string Label { get; set; }

    public string[] Parents { get; set; } = [];

    public JsonDocument? Attributes { get; set; }

    public int Sort { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public string? CreatedBy { get; set; }

    public string? UpdatedBy { get; set; }

    public uint Version { get; set; }

    public CatalogItem ToItem() => new(
        Id,
        Kind,
        Code,
        Label,
        Parents,
        Attributes?.RootElement.Clone(),
        Sort,
        new DateTimeOffset(DateTime.SpecifyKind(CreatedAtUtc, DateTimeKind.Utc)),
        new DateTimeOffset(DateTime.SpecifyKind(UpdatedAtUtc, DateTimeKind.Utc)),
        CreatedBy,
        UpdatedBy,
        Version);

    public static CatalogEntity From(CatalogItem item) => new()
    {
        Id = item.Id,
        Kind = item.Kind,
        Code = item.Code,
        Label = item.Label,
        Parents = [.. item.Parents],
        Attributes = ToDocument(item.Attributes),
        Sort = item.Sort,
        CreatedAtUtc = item.CreatedAt.UtcDateTime,
        UpdatedAtUtc = item.UpdatedAt.UtcDateTime,
        CreatedBy = item.CreatedBy,
        UpdatedBy = item.UpdatedBy,
    };

    public void Apply(CatalogItem item)
    {
        Label = item.Label;
        Parents = [.. item.Parents];
        Attributes = ToDocument(item.Attributes);
        Sort = item.Sort;
        UpdatedAtUtc = item.UpdatedAt.UtcDateTime;
        UpdatedBy = item.UpdatedBy;
    }

    private static JsonDocument? ToDocument(JsonElement? element)
        => element is { ValueKind: JsonValueKind.Object } value ? JsonDocument.Parse(value.GetRawText()) : null;
}
