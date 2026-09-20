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

    public AuditEntry ToItem() => new(
        Id,
        new DateTimeOffset(DateTime.SpecifyKind(AtUtc, DateTimeKind.Utc)),
        Actor,
        Action,
        Resource,
        Kind,
        TargetId,
        Summary);
}
