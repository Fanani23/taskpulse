namespace TaskPulse.Api.Models;

public enum TaskItemStatus
{
    Todo,
    InProgress,
    Done,
}

public enum TaskPriority
{
    Low,
    Normal,
    High,
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
    uint Version = 0,
    TaskPriority Priority = TaskPriority.Normal,
    DateTimeOffset? DueAt = null,
    string? AssigneeId = null,
    string? AssigneeName = null,
    IReadOnlyList<string>? Labels = null)
{
    public IReadOnlyList<string> Labels { get; init; } = Labels ?? [];

    // Records compare lists by reference; two items with the same labels are the same item.
    public bool Equals(TaskItem? other)
        => other is not null
           && (Id, Title, Description, Status, CreatedAt, UpdatedAt, CreatedBy, UpdatedBy, DeletedAt, Version, Priority, DueAt, AssigneeId, AssigneeName)
              == (other.Id, other.Title, other.Description, other.Status, other.CreatedAt, other.UpdatedAt, other.CreatedBy, other.UpdatedBy, other.DeletedAt, other.Version, other.Priority, other.DueAt, other.AssigneeId, other.AssigneeName)
           && Labels.SequenceEqual(other.Labels, StringComparer.Ordinal);

    public override int GetHashCode()
        => HashCode.Combine(Id, Title, Status, UpdatedAt, Version, Priority, DueAt, string.Join(",", Labels));

    // Open past its due date - computed against the clock the caller passes so it is testable.
    public bool IsOverdue(DateTimeOffset now) => Status != TaskItemStatus.Done && DeletedAt is null && DueAt is { } due && due < now;
}
