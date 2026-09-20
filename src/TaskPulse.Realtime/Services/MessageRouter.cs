using System.Net.WebSockets;
using System.Text.Json;
using Microsoft.Extensions.Options;
using TaskPulse.Realtime.Infrastructure;
using TaskPulse.Realtime.Models;

namespace TaskPulse.Realtime.Services;

public sealed class MessageRouter(ConnectionManager connections, TokenValidator tokens, NodeBus nodes, IOptions<WsOptions> options, ILogger<MessageRouter> logger)
{
    public async Task RouteAsync(WsConnection sender, string text, CancellationToken cancellationToken)
    {
        sender.MarkReceived();

        var limits = options.Value;
        switch (sender.Messages.Hit(limits.MessagesPerMinute))
        {
            case RateGateVerdict.Limited:
                await sender.SendAsync(ServerMessage.Fault($"Too many messages: at most {limits.MessagesPerMinute} per minute."), cancellationToken);
                return;
            case RateGateVerdict.Abusive:
                logger.LogWarning("Closing {ConnectionId}: kept sending past the message limit", sender.Id);
                await sender.CloseAsync(WebSocketCloseStatus.PolicyViolation, "Message rate limit exceeded");
                return;
        }

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
            await sender.SendAsync(ServerMessage.Fault("Malformed JSON. Expected {\"type\":\"echo|broadcast|ping|auth\",\"data\":\"...\"}"), cancellationToken);
            return;
        }

        switch (message?.Type?.ToLowerInvariant())
        {
            case "echo":
                await sender.SendAsync(ServerMessage.Echo(sender.Id, message.Data), cancellationToken);
                break;

            case "auth":
                await AuthenticateAsync(sender, message.Token ?? message.Data, cancellationToken);
                break;

            case "broadcast":
                await BroadcastAsync(sender, message.Data, cancellationToken);
                break;

            case "ping":
                await sender.SendAsync(ServerMessage.Pong(sender.Id), cancellationToken);
                break;

            default:
                await sender.SendAsync(ServerMessage.Fault($"Unknown message type '{message?.Type}'. Expected echo, broadcast, ping or auth."), cancellationToken);
                break;
        }
    }

    // {"type":"auth","token":"<access token from the Vue + Express sign-in>"} attaches an identity to the connection.
    private async Task AuthenticateAsync(WsConnection sender, string? token, CancellationToken cancellationToken)
    {
        if (!tokens.Enabled)
        {
            await sender.SendAsync(ServerMessage.Fault("Sign-in is not configured on this server (WebSocket:JwtSecret)."), cancellationToken);
            return;
        }

        var identity = await tokens.ValidateAsync(token);
        if (identity is null)
        {
            sender.Identity = null;
            await sender.SendAsync(ServerMessage.Fault("Invalid or expired token."), cancellationToken);
            return;
        }

        sender.Identity = identity;
        logger.LogInformation("{ConnectionId} authenticated as {Actor}", sender.Id, identity.Actor);
        await sender.SendAsync(ServerMessage.Authed(sender.Id, identity.Actor), cancellationToken);
    }

    // Anyone may listen; only a signed-in connection may talk to everyone, and not more than BroadcastsPerMinute.
    private async Task BroadcastAsync(WsConnection sender, string? data, CancellationToken cancellationToken)
    {
        if (sender.Identity is null)
        {
            await sender.SendAsync(ServerMessage.Fault("Sign in to broadcast: send {\"type\":\"auth\",\"token\":\"<access token>\"} first."), cancellationToken);
            return;
        }

        var limit = options.Value.BroadcastsPerMinute;
        if (sender.Broadcasts.Hit(limit) != RateGateVerdict.Allowed)
        {
            await sender.SendAsync(ServerMessage.Fault($"Too many broadcasts: at most {limit} per minute."), cancellationToken);
            return;
        }

        logger.LogInformation("Broadcast from {ConnectionId} ({Actor}) to {Recipients} connection(s)", sender.Id, sender.Identity.Actor, connections.Count);
        await connections.BroadcastAsync(ServerMessage.Broadcast(sender.Id, data, sender.Identity.Actor), exceptId: null, cancellationToken);
        await nodes.PublishAsync(sender.Id, data, sender.Identity.Actor, cancellationToken); // and to every other node's sockets
    }
}
