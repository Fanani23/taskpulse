using System.Text.Json;
using TaskPulse.Realtime.Models;

namespace TaskPulse.Realtime.Services;

public sealed class MessageRouter(ConnectionManager connections, ILogger<MessageRouter> logger)
{
    public async Task RouteAsync(WsConnection sender, string text, CancellationToken cancellationToken)
    {
        sender.MarkReceived();

        if (!text.TrimStart().StartsWith('{'))
        {
            await sender.SendAsync(ServerMessage.Echo(sender.Id, text), cancellationToken);
            return;
        }

        ClientMessage? message;
        try
        {
            message = ServerMessage.ParseClient(text);
        }
        catch (JsonException ex)
        {
            logger.LogDebug(ex, "Malformed JSON from {ConnectionId}", sender.Id);
            await sender.SendAsync(ServerMessage.Fault("Malformed JSON. Expected {\"type\":\"echo|broadcast|ping\",\"data\":\"...\"}"), cancellationToken);
            return;
        }

        switch (message?.Type?.ToLowerInvariant())
        {
            case "echo":
                await sender.SendAsync(ServerMessage.Echo(sender.Id, message.Data), cancellationToken);
                break;

            case "broadcast":
                logger.LogInformation("Broadcast from {ConnectionId} to {Recipients} connection(s)", sender.Id, connections.Count);
                await connections.BroadcastAsync(ServerMessage.Broadcast(sender.Id, message.Data), exceptId: null, cancellationToken);
                break;

            case "ping":
                await sender.SendAsync(ServerMessage.Pong(sender.Id), cancellationToken);
                break;

            default:
                await sender.SendAsync(ServerMessage.Fault($"Unknown message type '{message?.Type}'. Expected echo, broadcast or ping."), cancellationToken);
                break;
        }
    }
}
