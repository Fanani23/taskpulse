using TaskPulse.Api.Models;

namespace TaskPulse.Api.Data;

public sealed class TaskEntity
{
    public Guid Id { get; set; }

    public required string Title { get; set; }

    public string? Description { get; set; }

    public TaskItemStatus Status { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public uint Version { get; set; }

    public TaskItem ToItem() => new(
        Id,
        Title,
        Description,
        Status,
        new DateTimeOffset(DateTime.SpecifyKind(CreatedAtUtc, DateTimeKind.Utc)),
        new DateTimeOffset(DateTime.SpecifyKind(UpdatedAtUtc, DateTimeKind.Utc)));

    public static TaskEntity From(TaskItem item) => new()
    {
        Id = item.Id,
        Title = item.Title,
        Description = item.Description,
        Status = item.Status,
        CreatedAtUtc = item.CreatedAt.UtcDateTime,
        UpdatedAtUtc = item.UpdatedAt.UtcDateTime,
    };

    public void Apply(TaskItem item)
    {
        Title = item.Title;
        Description = item.Description;
        Status = item.Status;
        UpdatedAtUtc = item.UpdatedAt.UtcDateTime;
    }
}
