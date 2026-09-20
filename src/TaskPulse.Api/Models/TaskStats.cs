using System.ComponentModel.DataAnnotations;

namespace TaskPulse.Api.Models;

public sealed record TaskStatsQuery
{
    [Range(TaskLimits.StatsMinDays, TaskLimits.StatsMaxDays, ErrorMessage = "days must be between {1} and {2}.")]
    public int? Days { get; init; }
}

public sealed record DailyTaskCount(DateOnly Date, int Created, int Done);

public sealed record OpenTaskSummary(Guid Id, string Title, TaskItemStatus Status, DateTimeOffset CreatedAt);

public sealed record TaskStats(
    int Total,
    IReadOnlyDictionary<TaskItemStatus, int> ByStatus,
    double CompletionRate,
    int CreatedToday,
    int DoneThisWeek,
    int DonePreviousWeek,
    OpenTaskSummary? OldestOpen,
    IReadOnlyList<DailyTaskCount> Daily);
