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
    DateTimeOffset UpdatedAt,
    string? CreatedBy = null,
    string? UpdatedBy = null,
    DateTimeOffset? DeletedAt = null,
    uint Version = 0);
