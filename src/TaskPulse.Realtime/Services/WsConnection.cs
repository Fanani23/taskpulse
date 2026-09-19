using TaskPulse.Realtime.Models;
using System.Net.WebSockets;
using System.Text;

namespace TaskPulse.Realtime.Services;

public sealed class WsConnection(string id, WebSocket socket, string? remoteAddress) : IDisposable
{
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private long _received;
    private long _sent;

    public string Id { get; } = id;
    public WebSocket Socket { get; } = socket;
    public string? RemoteAddress { get; } = remoteAddress;
    public DateTimeOffset ConnectedAt { get; } = DateTimeOffset.UtcNow;

    public long MessagesReceived => Interlocked.Read(ref _received);
    public long MessagesSent => Interlocked.Read(ref _sent);

    public void MarkReceived() => Interlocked.Increment(ref _received);

    public async Task SendAsync(ServerMessage message, CancellationToken cancellationToken)
    {
        if (Socket.State != WebSocketState.Open)
        {
            return;
        }

        var bytes = Encoding.UTF8.GetBytes(message.ToJson());

        await _sendLock.WaitAsync(cancellationToken);
        try
        {
            if (Socket.State == WebSocketState.Open)
            {
                await Socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, cancellationToken);
                Interlocked.Increment(ref _sent);
            }
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public async Task BeginCloseAsync(WebSocketCloseStatus status, string reason)
    {
        if (Socket.State != WebSocketState.Open)
        {
            return;
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try
        {
            await _sendLock.WaitAsync(timeout.Token);
            try
            {
                if (Socket.State == WebSocketState.Open)
                {
                    await Socket.CloseOutputAsync(status, reason, timeout.Token);
                }
            }
            finally
            {
                _sendLock.Release();
            }
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or ObjectDisposedException)
        {
        }
    }

    public async Task CloseAsync(WebSocketCloseStatus status, string reason)
    {
        if (Socket.State is not (WebSocketState.Open or WebSocketState.CloseReceived))
        {
            return;
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try
        {
            await Socket.CloseAsync(status, reason, timeout.Token);
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or ObjectDisposedException)
        {
        }
    }

    public ConnectionStats ToStats() => new(Id, RemoteAddress, ConnectedAt, MessagesReceived, MessagesSent);

    public void Dispose() => _sendLock.Dispose();
}
