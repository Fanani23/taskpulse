namespace TaskPulse.Api.Data;

// One Idempotency-Key as used by one caller on one route: the stored response is what every repeat gets back.
public sealed class IdempotencyKeyEntity
{
    public long Id { get; set; }

    // "<sub>|<METHOD> <path>"
    public required string Scope { get; set; }

    public required string Key { get; set; }

    // hash of the bound request; a repeat with another body is refused
    public required string Fingerprint { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    // null while the first request is still running
    public int? Status { get; set; }

    public string? Body { get; set; }

    public string? ContentType { get; set; }

    public string? Location { get; set; }
}
