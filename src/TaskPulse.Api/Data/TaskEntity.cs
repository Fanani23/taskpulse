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

    public DateTime? DeletedAtUtc { get; set; }

    public string? CreatedBy { get; set; }

    public string? UpdatedBy { get; set; }

    public uint Version { get; set; }

    public TaskItem ToItem() => new(
        Id,
        Title,
        Description,
        Status,
        Utc(CreatedAtUtc),
        Utc(UpdatedAtUtc),
        CreatedBy,
        UpdatedBy,
        DeletedAtUtc is { } deleted ? Utc(deleted) : null,
        Version);

    public static TaskEntity From(TaskItem item) => new()
    {
        Id = item.Id,
        Title = item.Title,
        Description = item.Description,
        Status = item.Status,
        CreatedAtUtc = item.CreatedAt.UtcDateTime,
        UpdatedAtUtc = item.UpdatedAt.UtcDateTime,
        DeletedAtUtc = item.DeletedAt?.UtcDateTime,
        CreatedBy = item.CreatedBy,
        UpdatedBy = item.UpdatedBy,
    };

    public void Apply(TaskItem item)
    {
        Title = item.Title;
        Description = item.Description;
        Status = item.Status;
        UpdatedAtUtc = item.UpdatedAt.UtcDateTime;
        DeletedAtUtc = item.DeletedAt?.UtcDateTime;
        UpdatedBy = item.UpdatedBy;
    }

    private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
