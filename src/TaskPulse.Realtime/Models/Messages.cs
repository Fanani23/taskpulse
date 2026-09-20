using System.Text.Json;
using System.Text.Json.Serialization;

namespace TaskPulse.Realtime.Models;

public sealed record ClientMessage(string? Type, string? Data, string? Token);

public sealed record ServerMessage(
    string Type,
    DateTimeOffset Ts,
    string? ConnectionId = null,
    string? From = null,
    string? Data = null,
    string? Event = null,
    int? Connections = null,
    string? Error = null,
    string? Resource = null,
    string? Action = null,
    string? Id = null,
    string? Kind = null,
    string? Actor = null,
    string? User = null)
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

    public static ServerMessage Broadcast(string from, string? data, string? actor)
        => new("broadcast", DateTimeOffset.UtcNow, From: from, Data: data, Actor: actor);

    public static ServerMessage Authed(string connectionId, string user)
        => new("authed", DateTimeOffset.UtcNow, ConnectionId: connectionId, User: user);

    public static ServerMessage Pong(string from)
        => new("pong", DateTimeOffset.UtcNow, From: from);

    public static ServerMessage System(string @event, string connectionId, int connections)
        => new("system", DateTimeOffset.UtcNow, ConnectionId: connectionId, Event: @event, Connections: connections);

    public static ServerMessage Fault(string error)
        => new("error", DateTimeOffset.UtcNow, Error: error);

    public static ServerMessage Changed(ChangeNotification change)
        => new("changed", DateTimeOffset.UtcNow, Resource: change.Resource, Action: change.Action, Id: change.Id, Kind: change.Kind, Actor: change.Actor);
}

// Posted by TaskPulse.Api after every write (loopback only) so that every open page can refresh.
public sealed record ChangeNotification(string Resource, string Action, string Id, string? Kind, string? Actor);

public sealed record ConnectionStats(
    string Id,
    string? RemoteAddress,
    DateTimeOffset ConnectedAt,
    long MessagesReceived,
    long MessagesSent,
    string? User);

public sealed record ServerStats(
    int Connections,
    DateTimeOffset StartedAt,
    double UptimeSeconds,
    IReadOnlyList<ConnectionStats> Clients,
    string? Node = null,
    bool Redis = false,
    long RelayedBroadcasts = 0,
    long StreamEventsDelivered = 0);
