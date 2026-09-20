using System.ComponentModel.DataAnnotations;

namespace TaskPulse.Realtime.Infrastructure;

public sealed class WsOptions
{
    public const string SectionName = "WebSocket";

    [Range(5, 300)]
    public int KeepAliveIntervalSeconds { get; set; } = 30;

    [Range(1024, 1024 * 1024)]
    public int ReceiveBufferBytes { get; set; } = 4096;

    [Range(1024, 16 * 1024 * 1024)]
    public int MaxMessageBytes { get; set; } = 64 * 1024;

    // Optional shared secret for /internal/broadcast when the API is not on the same host (Docker compose):
    // a request carrying it in X-Internal-Token is accepted from any address. Empty = loopback only.
    public string? InternalToken { get; set; }
}
