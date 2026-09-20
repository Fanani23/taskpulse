using System.ComponentModel.DataAnnotations;

namespace TaskPulse.Api.Models;

public static class AuditLimits
{
    public const int ActorMaxLength = 120;
    public const int ActionMaxLength = 16;
    public const int ResourceMaxLength = 32;
    public const int KindMaxLength = 64;
    public const int TargetMaxLength = 64;
    public const int SummaryMaxLength = 200;
    public const int ListMax = 200;
    public const int ListDefault = 20;
}

public sealed record AuditEntry(
    long Id,
    DateTimeOffset At,
    string? Actor,
    string Action,
    string Resource,
    string? Kind,
    string TargetId,
    string Summary,
    IReadOnlyDictionary<string, FieldChange>? Changes = null);

// One field before and after an update; catalog attributes appear as `attributes.<key>`.
public sealed record FieldChange(object? From, object? To);

// An event another service reports (part A's sign-ins): the actor is given, not taken from a bearer token.
public sealed record ExternalAuditEvent
{
    [StringLength(AuditLimits.ActorMaxLength)]
    public string? Actor { get; init; }

    [Required, RegularExpression("^[a-z-]{1,16}$", ErrorMessage = "action must be a short lowercase word.")]
    public string? Action { get; init; }

    [Required, RegularExpression("^[a-z]{1,32}$", ErrorMessage = "resource must be a short lowercase word.")]
    public string? Resource { get; init; }

    [StringLength(AuditLimits.KindMaxLength)]
    public string? Kind { get; init; }

    [Required, StringLength(AuditLimits.TargetMaxLength)]
    public string? TargetId { get; init; }

    [Required, StringLength(AuditLimits.SummaryMaxLength)]
    public string? Summary { get; init; }
}

public sealed record AuditListQuery
{
    [RegularExpression("^[a-z]{1,32}$", ErrorMessage = "resource must be a short lowercase word.")]
    public string? Resource { get; init; }

    [StringLength(AuditLimits.KindMaxLength)]
    public string? Kind { get; init; }

    // one record's history: its id (task) or code (catalog item)
    [StringLength(AuditLimits.TargetMaxLength)]
    public string? Target { get; init; }

    [Range(1, AuditLimits.ListMax, ErrorMessage = "limit must be between {1} and {2}.")]
    public int? Limit { get; init; }
}
