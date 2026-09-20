using TaskPulse.Api.Models;

namespace TaskPulse.Api.Data;

public sealed class WebhookEntity
{
    public Guid Id { get; set; }

    public required string Url { get; set; }

    // Never returned by the API; signs every delivery (HMAC-SHA256 of the body).
    public required string Secret { get; set; }

    // Resources this hook wants ("task", "catalog", "upload", …); empty = everything.
    public string[] Resources { get; set; } = [];

    public bool Active { get; set; } = true;

    public string? Description { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public string? CreatedBy { get; set; }

    public DateTime? LastAttemptAtUtc { get; set; }

    public int? LastStatus { get; set; }

    public int ConsecutiveFailures { get; set; }

    public Webhook ToItem() => new(
        Id,
        Url,
        Resources,
        Active,
        Description,
        new DateTimeOffset(DateTime.SpecifyKind(CreatedAtUtc, DateTimeKind.Utc)),
        CreatedBy,
        LastAttemptAtUtc is { } at ? new DateTimeOffset(DateTime.SpecifyKind(at, DateTimeKind.Utc)) : null,
        LastStatus,
        ConsecutiveFailures);
}

public sealed class WebhookDeliveryEntity
{
    public long Id { get; set; }

    public Guid WebhookId { get; set; }

    public DateTime AtUtc { get; set; }

    public required string Event { get; set; }

    public required string Payload { get; set; }

    public int Attempts { get; set; }

    public int? Status { get; set; }

    public string? Error { get; set; }

    public bool Delivered { get; set; }

    public WebhookDelivery ToItem() => new(Id, new DateTimeOffset(DateTime.SpecifyKind(AtUtc, DateTimeKind.Utc)), Event, Attempts, Status, Error, Delivered);
}
