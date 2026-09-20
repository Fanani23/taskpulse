using TaskPulse.Api.Models;

namespace TaskPulse.Api.Data;

public sealed class UploadEntity
{
    public Guid Id { get; set; }

    public required string FileName { get; set; }

    public required string ContentType { get; set; }

    public long Size { get; set; }

    public string? Source { get; set; }

    public string? Note { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public string? OwnerId { get; set; }

    public UploadItem ToItem() => new(
        Id,
        FileName,
        ContentType,
        Size,
        Source,
        Note,
        new DateTimeOffset(DateTime.SpecifyKind(CreatedAtUtc, DateTimeKind.Utc)),
        OwnerId);

    public static UploadEntity From(UploadItem item) => new()
    {
        Id = item.Id,
        FileName = item.FileName,
        ContentType = item.ContentType,
        Size = item.Size,
        Source = item.Source,
        Note = item.Note,
        CreatedAtUtc = item.CreatedAt.UtcDateTime,
        OwnerId = item.OwnerId,
    };
}
