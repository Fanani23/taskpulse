using System.ComponentModel.DataAnnotations;

namespace TaskPulse.Api.Models;

public static class UploadLimits
{
    public const int FileNameMaxLength = 200;
    public const int SourceMaxLength = 32;
    public const int NoteMaxLength = 500;
    public const int FilesPerRequestMax = 10;
    public const int ListMax = 100;
    public const string SourcePattern = "^[a-z0-9][a-z0-9-]{0,31}$";

    public static readonly IReadOnlyDictionary<string, string> Extensions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["image/png"] = ".png",
        ["image/jpeg"] = ".jpg",
        ["image/webp"] = ".webp",
        ["application/pdf"] = ".pdf",
        ["text/plain"] = ".txt",
    };
}

public sealed record UploadItem(
    Guid Id,
    string FileName,
    string ContentType,
    long Size,
    string? Source,
    string? Note,
    DateTimeOffset CreatedAt);

public sealed record UploadListQuery
{
    [RegularExpression(UploadLimits.SourcePattern, ErrorMessage = "source must be a short lowercase tag.")]
    public string? Source { get; init; }
}

public sealed record UploadForm
{
    [Required(ErrorMessage = "At least one file is required.")]
    public IFormFileCollection? Files { get; init; }

    [RegularExpression(UploadLimits.SourcePattern, ErrorMessage = "source must be a short lowercase tag.")]
    public string? Source { get; init; }

    [StringLength(UploadLimits.NoteMaxLength, ErrorMessage = "note must be at most {1} characters.")]
    public string? Note { get; init; }
}
