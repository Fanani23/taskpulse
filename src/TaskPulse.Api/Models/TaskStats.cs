using System.ComponentModel.DataAnnotations;

namespace TaskPulse.Api.Models;

public sealed record TaskStatsQuery
{
    [Range(TaskLimits.StatsMinDays, TaskLimits.StatsMaxDays, ErrorMessage = "days must be between {1} and {2}.")]
    public int? Days { get; init; }
}

public sealed record DailyTaskCount(DateOnly Date, int Created, int Done);

public sealed record TaskSummary(Guid Id, string Title, TaskItemStatus Status, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public sealed record TaskStats(
    int Total,
    IReadOnlyDictionary<TaskItemStatus, int> ByStatus,
    double CompletionRate,
    int CreatedToday,
    int DoneThisWeek,
    int DonePreviousWeek,
    int Overdue,
    int DueThisWeek,
    TaskSummary? OldestOpen,
    IReadOnlyList<TaskSummary> RecentlyUpdated,
    IReadOnlyList<DailyTaskCount> Daily);
