using System.Net.WebSockets;
using System.Text;
using TaskPulse.Realtime.Infrastructure;
using TaskPulse.Realtime.Models;
using Microsoft.Extensions.Options;

namespace TaskPulse.Realtime.Services;

public sealed class WebSocketSession(
    ConnectionManager connections,
    MessageRouter router,
    IOptions<WsOptions> options,
    IHostApplicationLifetime lifetime,
    ILogger<WebSocketSession> logger)
{
    public async Task RunAsync(WebSocket socket, string? remoteAddress, CancellationToken requestAborted)
    {
        using var connection = connections.Add(socket, remoteAddress);

        using var shutdown = lifetime.ApplicationStopping.Register(
            () => _ = connection.BeginCloseAsync(WebSocketCloseStatus.EndpointUnavailable, "Server shutting down"));

        using var scope = logger.BeginScope("ConnectionId:{ConnectionId}", connection.Id);
        logger.LogInformation("Connected from {RemoteAddress} ({Connections} total)", connection.RemoteAddress, connections.Count);

        try
        {
            await connection.SendAsync(ServerMessage.Welcome(connection.Id, connections.Count), requestAborted);
            await connections.BroadcastAsync(ServerMessage.System("joined", connection.Id, connections.Count), connection.Id, requestAborted);

            await ReceiveLoopAsync(connection, requestAborted);
        }
        catch (OperationCanceledException)
        {
        }
        catch (WebSocketException ex)
        {
            logger.LogWarning("Socket error: {Message} ({Code})", ex.Message, ex.WebSocketErrorCode);
        }
        finally
        {
            connections.Remove(connection.Id);
            await connection.CloseAsync(WebSocketCloseStatus.NormalClosure, "Bye");

            await connections.BroadcastAsync(ServerMessage.System("left", connection.Id, connections.Count), null, CancellationToken.None);
            logger.LogInformation("Disconnected after {Received} received / {Sent} sent ({Connections} remaining)",
                connection.MessagesReceived, connection.MessagesSent, connections.Count);
        }
    }

    private async Task ReceiveLoopAsync(WsConnection connection, CancellationToken cancellationToken)
    {
        var limits = options.Value;
        var buffer = new byte[limits.ReceiveBufferBytes];
        using var message = new MemoryStream();

        while (connection.Socket.State is WebSocketState.Open or WebSocketState.CloseSent)
        {
            message.SetLength(0);
            WebSocketReceiveResult result;

            do
            {
                result = await connection.Socket.ReceiveAsync(buffer, cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    return;
                }

                message.Write(buffer, 0, result.Count);
                if (message.Length > limits.MaxMessageBytes)
                {
                    await connection.CloseAsync(WebSocketCloseStatus.MessageTooBig, $"Message exceeds {limits.MaxMessageBytes} bytes");
                    return;
                }
            }
            while (!result.EndOfMessage);

            if (connection.Socket.State != WebSocketState.Open)
            {
                continue;
            }

            if (result.MessageType != WebSocketMessageType.Text)
            {
                await connection.SendAsync(ServerMessage.Fault("Only text frames are supported."), cancellationToken);
                continue;
            }

            var text = Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length);
            await router.RouteAsync(connection, text, cancellationToken);
        }
    }
}
