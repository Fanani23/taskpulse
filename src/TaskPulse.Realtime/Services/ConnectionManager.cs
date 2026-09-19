using TaskPulse.Realtime.Models;
using System.Collections.Concurrent;
using System.Net.WebSockets;

namespace TaskPulse.Realtime.Services;

public sealed class ConnectionManager(ILogger<ConnectionManager> logger)
{
    private readonly ConcurrentDictionary<string, WsConnection> _connections = new();
    private readonly DateTimeOffset _startedAt = DateTimeOffset.UtcNow;

    public int Count => _connections.Count;

    public WsConnection Add(WebSocket socket, string? remoteAddress)
    {
        var connection = new WsConnection(Guid.NewGuid().ToString("N")[..12], socket, remoteAddress);
        _connections[connection.Id] = connection;
        return connection;
    }

    public bool Remove(string id) => _connections.TryRemove(id, out _);

    public async Task BroadcastAsync(ServerMessage message, string? exceptId, CancellationToken cancellationToken)
    {
        var sends = _connections.Values
            .Where(connection => connection.Id != exceptId)
            .Select(connection => SendQuietlyAsync(connection, message, cancellationToken));

        await Task.WhenAll(sends);
    }

    public Task CloseAllAsync(string reason)
        => Task.WhenAll(_connections.Values.Select(connection =>
            connection.CloseAsync(WebSocketCloseStatus.EndpointUnavailable, reason)));

    public ServerStats GetStats()
    {
        var now = DateTimeOffset.UtcNow;
        return new ServerStats(
            Count,
            _startedAt,
            Math.Round((now - _startedAt).TotalSeconds, 1),
            _connections.Values.OrderBy(connection => connection.ConnectedAt).Select(connection => connection.ToStats()).ToList());
    }

    private async Task SendQuietlyAsync(WsConnection connection, ServerMessage message, CancellationToken cancellationToken)
    {
        try
        {
            await connection.SendAsync(message, cancellationToken);
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or ObjectDisposedException)
        {
            logger.LogDebug(ex, "Broadcast to {ConnectionId} skipped: socket no longer writable", connection.Id);
        }
    }
}
