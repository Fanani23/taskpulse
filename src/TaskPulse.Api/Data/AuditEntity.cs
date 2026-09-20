using TaskPulse.Api.Models;

namespace TaskPulse.Api.Data;

public sealed class AuditEntity
{
    public long Id { get; set; }

    public DateTime AtUtc { get; set; }

    public string? Actor { get; set; }

    public required string Action { get; set; }

    public required string Resource { get; set; }

    public string? Kind { get; set; }

    public required string TargetId { get; set; }

    public required string Summary { get; set; }

    // jsonb `{ field: {from, to} }` for updates; null for everything else
    public string? Changes { get; set; }

    public AuditEntry ToItem() => new(
        Id,
        new DateTimeOffset(DateTime.SpecifyKind(AtUtc, DateTimeKind.Utc)),
        Actor,
        Action,
        Resource,
        Kind,
        TargetId,
        Summary,
        Changes is null ? null : System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, FieldChange>>(Changes, Json));

    private static readonly System.Text.Json.JsonSerializerOptions Json = new(System.Text.Json.JsonSerializerDefaults.Web);
}
