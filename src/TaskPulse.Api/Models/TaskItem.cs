namespace TaskPulse.Api.Models;

public enum TaskItemStatus
{
    Todo,
    InProgress,
    Done,
}

public sealed record TaskItem(
    Guid Id,
    string Title,
    string? Description,
    TaskItemStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
