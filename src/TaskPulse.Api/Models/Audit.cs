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
    string Summary);

public sealed record AuditListQuery
{
    [RegularExpression("^[a-z]{1,32}$", ErrorMessage = "resource must be a short lowercase word.")]
    public string? Resource { get; init; }

    [Range(1, AuditLimits.ListMax, ErrorMessage = "limit must be between {1} and {2}.")]
    public int? Limit { get; init; }
}
