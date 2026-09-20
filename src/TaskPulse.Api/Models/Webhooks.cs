using System.ComponentModel.DataAnnotations;

namespace TaskPulse.Api.Models;

public static class WebhookLimits
{
    public const int UrlMaxLength = 500;
    public const int SecretMinLength = 16;
    public const int SecretMaxLength = 128;
    public const int DescriptionMaxLength = 200;
    public const int MaxResources = 8;
    public const int DeliveriesKept = 50;
    public const int Attempts = 3;
}

public sealed record Webhook(
    Guid Id,
    string Url,
    IReadOnlyList<string> Resources,
    bool Active,
    string? Description,
    DateTimeOffset CreatedAt,
    string? CreatedBy,
    DateTimeOffset? LastAttemptAt,
    int? LastStatus,
    int ConsecutiveFailures);

public sealed record WebhookDelivery(long Id, DateTimeOffset At, string Event, int Attempts, int? Status, string? Error, bool Delivered);

public sealed record WebhookRequest
{
    // https only, except loopback for local development
    [Required, StringLength(WebhookLimits.UrlMaxLength), Url]
    public string? Url { get; init; }

    // Shared with the receiver; every delivery carries X-TaskPulse-Signature: sha256=<hmac of the body>
    [Required, StringLength(WebhookLimits.SecretMaxLength, MinimumLength = WebhookLimits.SecretMinLength, ErrorMessage = "secret must be {2}-{1} characters.")]
    public string? Secret { get; init; }

    [MaxLength(WebhookLimits.MaxResources)]
    public string[]? Resources { get; init; }

    public bool? Active { get; init; }

    [StringLength(WebhookLimits.DescriptionMaxLength)]
    public string? Description { get; init; }
}

public sealed record WebhookUpdateRequest
{
    [StringLength(WebhookLimits.UrlMaxLength), Url]
    public string? Url { get; init; }

    [StringLength(WebhookLimits.SecretMaxLength, MinimumLength = WebhookLimits.SecretMinLength, ErrorMessage = "secret must be {2}-{1} characters.")]
    public string? Secret { get; init; }

    [MaxLength(WebhookLimits.MaxResources)]
    public string[]? Resources { get; init; }

    public bool? Active { get; init; }

    [StringLength(WebhookLimits.DescriptionMaxLength)]
    public string? Description { get; init; }
}
