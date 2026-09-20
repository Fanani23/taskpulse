using Microsoft.Extensions.Options;
using TaskPulse.Api.Infrastructure;
using TaskPulse.Api.Models;
using TaskPulse.Api.Repositories;

namespace TaskPulse.Api.Services;

public enum UploadRejection
{
    None,
    TooLarge,
    UnsupportedType,
    TooMany,
}

public sealed record UploadResult(IReadOnlyList<UploadItem> Items, UploadRejection Rejection, string? FileName);

public sealed record UploadContent(UploadItem Item, Stream Content) : IAsyncDisposable
{
    public ValueTask DisposeAsync() => Content.DisposeAsync();
}

public interface IUploadService
{
    Task<IReadOnlyList<UploadItem>> ListAsync(UploadListQuery query, CancellationToken cancellationToken = default);

    Task<UploadItem?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<UploadContent?> OpenAsync(Guid id, CancellationToken cancellationToken = default);

    Task<UploadResult> CreateAsync(UploadForm form, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}

public sealed class UploadService(
    IUploadRepository repository,
    IUploadStore store,
    IOptions<ApiOptions> options,
    TimeProvider clock,
    ILogger<UploadService> logger) : IUploadService
{
    public Task<IReadOnlyList<UploadItem>> ListAsync(UploadListQuery query, CancellationToken cancellationToken = default)
        => repository.ListAsync(query.Source, UploadLimits.ListMax, cancellationToken);

    public Task<UploadItem?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => repository.GetAsync(id, cancellationToken);

    public async Task<UploadContent?> OpenAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var item = await repository.GetAsync(id, cancellationToken);
        if (item is null)
        {
            return null;
        }

        var content = store.Open(id);
        return content is null ? null : new UploadContent(item, content);
    }

    public async Task<UploadResult> CreateAsync(UploadForm form, CancellationToken cancellationToken = default)
    {
        var files = form.Files!;
        if (files.Count > UploadLimits.FilesPerRequestMax)
        {
            return new UploadResult([], UploadRejection.TooMany, null);
        }

        foreach (var file in files)
        {
            if (file.Length > options.Value.MaxUploadBytes)
            {
                return new UploadResult([], UploadRejection.TooLarge, file.FileName);
            }

            if (!UploadLimits.Extensions.ContainsKey(file.ContentType) || !await MatchesSignatureAsync(file, cancellationToken))
            {
                return new UploadResult([], UploadRejection.UnsupportedType, file.FileName);
            }
        }

        var items = new List<UploadItem>(files.Count);
        foreach (var file in files)
        {
            var item = new UploadItem(
                Guid.NewGuid(),
                SafeFileName(file.FileName, file.ContentType),
                file.ContentType.ToLowerInvariant(),
                file.Length,
                form.Source,
                string.IsNullOrWhiteSpace(form.Note) ? null : form.Note.Trim(),
                Timestamps.ToMicroseconds(clock.GetUtcNow()));

            await using (var content = file.OpenReadStream())
            {
                await store.SaveAsync(item.Id, content, cancellationToken);
            }

            try
            {
                await repository.AddAsync(item, cancellationToken);
            }
            catch
            {
                store.Delete(item.Id);
                throw;
            }

            items.Add(item);
            logger.LogInformation("Upload {UploadId} stored ({Size} bytes, {ContentType})", item.Id, item.Size, item.ContentType);
        }

        return new UploadResult(items, UploadRejection.None, null);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (!await repository.DeleteAsync(id, cancellationToken))
        {
            return false;
        }

        store.Delete(id);
        logger.LogInformation("Upload {UploadId} deleted", id);
        return true;
    }

    private static string SafeFileName(string? name, string contentType)
    {
        var trimmed = Path.GetFileName(name ?? string.Empty).Trim();
        if (trimmed.Length == 0 || trimmed.Length > UploadLimits.FileNameMaxLength)
        {
            trimmed = "upload" + UploadLimits.Extensions[contentType];
        }

        return string.Concat(trimmed.Select(c => char.IsControl(c) ? '_' : c));
    }

    private static async Task<bool> MatchesSignatureAsync(IFormFile file, CancellationToken cancellationToken)
    {
        await using var stream = file.OpenReadStream();
        var head = new byte[12];
        var read = await stream.ReadAtLeastAsync(head, head.Length, throwOnEndOfStream: false, cancellationToken);
        var span = head.AsSpan(0, read);

        return file.ContentType.ToLowerInvariant() switch
        {
            "image/png" => span.StartsWith((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47]),
            "image/jpeg" => span.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xD8, 0xFF]),
            "image/webp" => span.Length >= 12 && span[..4].SequenceEqual("RIFF"u8) && span[8..12].SequenceEqual("WEBP"u8),
            "application/pdf" => span.StartsWith("%PDF"u8),
            "text/plain" => !span.Contains((byte)0),
            _ => false,
        };
    }
}
