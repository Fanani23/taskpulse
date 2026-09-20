using Microsoft.Extensions.Options;
using TaskPulse.Api.Infrastructure;

namespace TaskPulse.Api.Services;

public interface IUploadStore
{
    Task SaveAsync(Guid id, Stream content, CancellationToken cancellationToken = default);

    Stream? Open(Guid id);

    void Delete(Guid id);
}

public sealed class DiskUploadStore : IUploadStore
{
    private readonly string _root;

    public DiskUploadStore(IOptions<ApiOptions> options, IHostEnvironment environment)
    {
        var configured = options.Value.UploadDirectory;
        _root = Path.IsPathRooted(configured) ? configured : Path.Combine(environment.ContentRootPath, configured);
        Directory.CreateDirectory(_root);
    }

    public async Task SaveAsync(Guid id, Stream content, CancellationToken cancellationToken = default)
    {
        var path = PathFor(id);
        await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, useAsync: true);
        await content.CopyToAsync(file, cancellationToken);
    }

    public Stream? Open(Guid id)
    {
        var path = PathFor(id);
        return File.Exists(path)
            ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, useAsync: true)
            : null;
    }

    public void Delete(Guid id)
    {
        var path = PathFor(id);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private string PathFor(Guid id) => Path.Combine(_root, id.ToString("N"));
}
