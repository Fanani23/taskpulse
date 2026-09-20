using System.Text.Json;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using TaskPulse.Realtime.Infrastructure;
using TaskPulse.Realtime.Models;

namespace TaskPulse.Realtime.Services;

// User broadcasts across Realtime nodes. A message a client sends to one node is delivered to that node's sockets
// directly and PUBLISHed on a Redis channel; every other node receives it there and fans it out to its own sockets.
// The node's own id is carried in the payload so it never delivers its own message twice. Change events do not
// need this - they go through the stream (see ChangeStreamReader). Without Redis a broadcast stays on its node.
public sealed class NodeBus(ConnectionManager connections, IOptions<WsOptions> options, ILogger<NodeBus> logger) : BackgroundService
{
    public const string Channel = "taskpulse:broadcast";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly string _node = (options.Value.NodeName ?? Environment.MachineName) + "/" + Guid.NewGuid().ToString("N")[..8];
    private ISubscriber? _publisher;

    // Visible for /stats and tests: messages relayed in from other nodes.
    public long Relayed { get; private set; }

    public string NodeId => _node;

    public bool Connected => _publisher is not null;

    public async Task PublishAsync(string from, string? data, string? actor, CancellationToken cancellationToken)
    {
        if (_publisher is not { } publisher)
        {
            return;
        }

        try
        {
            var payload = JsonSerializer.Serialize(new Envelope(_node, from, data, actor), Json);
            await publisher.PublishAsync(RedisChannel.Literal(Channel), payload);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogWarning(e, "Broadcast from {ConnectionId} could not be relayed to other nodes", from);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var redisUrl = options.Value.RedisUrl;
        if (string.IsNullOrWhiteSpace(redisUrl))
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var config = ConfigurationOptions.Parse(redisUrl);
                config.AbortOnConnectFail = false;
                using var redis = await ConnectionMultiplexer.ConnectAsync(config);
                var subscriber = redis.GetSubscriber();
                await subscriber.SubscribeAsync(RedisChannel.Literal(Channel), (channel, value) => { _ = RelayAsync(value, stoppingToken); });
                _publisher = subscriber;
                logger.LogInformation("Node {Node} relays broadcasts through Redis channel {Channel}", _node, Channel);
                await Task.Delay(Timeout.Infinite, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e)
            {
                _publisher = null;
                logger.LogWarning(e, "Node bus lost Redis ({Endpoint}); retrying in 5 s", redisUrl);
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    private async Task RelayAsync(RedisValue value, CancellationToken cancellationToken)
    {
        Envelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<Envelope>((string)value!, Json);
        }
        catch (JsonException e)
        {
            logger.LogWarning(e, "Skipping a malformed relayed broadcast");
            return;
        }

        if (envelope is null || envelope.Node == _node)
        {
            return; // our own, already delivered locally
        }

        Relayed++;
        await connections.BroadcastAsync(ServerMessage.Broadcast(envelope.From, envelope.Data, envelope.Actor), exceptId: null, cancellationToken);
    }

    private sealed record Envelope(string Node, string From, string? Data, string? Actor);
}
