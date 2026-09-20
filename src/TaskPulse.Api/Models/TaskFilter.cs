namespace TaskPulse.Api.Models;

public enum TaskDueFilter
{
    Any,
    Overdue,
    Today,
    Week,
    None,
}

// The list filters beyond status/search, resolved by the service ("me" -> the caller's id, "overdue" -> the enum).
public sealed record TaskFilter(TaskPriority? Priority, string? AssigneeId, string? Label, TaskDueFilter Due, DateTimeOffset Now);
