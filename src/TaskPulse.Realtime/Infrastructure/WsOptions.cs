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

    // The HS256 secret the Vue + Express sign-in signs access tokens with (same value as Api:JwtSecret). A client
    // sends {"type":"auth","token":"..."} to attach its identity to the connection; `broadcast` needs it. Empty =
    // nobody can broadcast (echo, ping and change events still work).
    public string? JwtSecret { get; set; }

    public string? JwtIssuer { get; set; }

    public string? JwtAudience { get; set; }

    // Per-connection limits, fixed one-minute windows. Past MessagesPerMinute the message is answered with an
    // error; past twice that the connection is closed with 1008 (policy violation).
    [Range(10, 100_000)]
    public int MessagesPerMinute { get; set; } = 120;

    [Range(1, 10_000)]
    public int BroadcastsPerMinute { get; set; } = 30;

    // When set, change events are read from the Redis stream the API writes (see ChangeStreamReader) instead of
    // arriving on /internal/broadcast. NodeName keys this node's stream cursor (defaults to the machine name).
    public string? RedisUrl { get; set; }

    public string? NodeName { get; set; }
}
