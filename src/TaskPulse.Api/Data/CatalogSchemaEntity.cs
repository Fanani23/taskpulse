using System.Text.Json;

namespace TaskPulse.Api.Data;

public sealed class CatalogSchemaEntity
{
    public required string Kind { get; set; }

    public required JsonDocument Schema { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public string? UpdatedBy { get; set; }
}
