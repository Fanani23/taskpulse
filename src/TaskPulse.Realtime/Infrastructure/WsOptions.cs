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
}
