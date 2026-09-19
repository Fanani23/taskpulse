using System.Text.Json;
using System.Text.Json.Serialization;

namespace TaskPulse.Realtime.Models;

public sealed record ClientMessage(string? Type, string? Data);

public sealed record ServerMessage(
    string Type,
    DateTimeOffset Ts,
    string? ConnectionId = null,
    string? From = null,
    string? Data = null,
    string? Event = null,
    int? Connections = null,
    string? Error = null)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    public static ClientMessage? ParseClient(string text)
        => JsonSerializer.Deserialize<ClientMessage>(text, Json);

    public static ServerMessage Welcome(string connectionId, int connections)
        => new("welcome", DateTimeOffset.UtcNow, ConnectionId: connectionId, Connections: connections);

    public static ServerMessage Echo(string from, string? data)
        => new("echo", DateTimeOffset.UtcNow, From: from, Data: data);

    public static ServerMessage Broadcast(string from, string? data)
        => new("broadcast", DateTimeOffset.UtcNow, From: from, Data: data);

    public static ServerMessage Pong(string from)
        => new("pong", DateTimeOffset.UtcNow, From: from);

    public static ServerMessage System(string @event, string connectionId, int connections)
        => new("system", DateTimeOffset.UtcNow, ConnectionId: connectionId, Event: @event, Connections: connections);

    public static ServerMessage Fault(string error)
        => new("error", DateTimeOffset.UtcNow, Error: error);
}

public sealed record ConnectionStats(
    string Id,
    string? RemoteAddress,
    DateTimeOffset ConnectedAt,
    long MessagesReceived,
    long MessagesSent);

public sealed record ServerStats(
    int Connections,
    DateTimeOffset StartedAt,
    double UptimeSeconds,
    IReadOnlyList<ConnectionStats> Clients);
