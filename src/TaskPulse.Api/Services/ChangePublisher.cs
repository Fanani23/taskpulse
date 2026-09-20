using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using TaskPulse.Api.Infrastructure;

namespace TaskPulse.Api.Services;

public sealed record ChangeEvent(string Resource, string Action, string Id, string? Kind, string? Actor);

public interface IChangePublisher
{
    void Publish(ChangeEvent change);
}

// Fan-out of "something changed" to TaskPulse.Realtime. Fire-and-forget: a write must never fail or slow down
// because the socket server is down, so events go through a bounded channel drained by a background service; on
// overflow the oldest event is dropped (clients re-read state anyway).
public sealed class ChangePublisher : IChangePublisher
{
    private readonly Channel<ChangeEvent> _channel = Channel.CreateBounded<ChangeEvent>(new BoundedChannelOptions(256) { FullMode = BoundedChannelFullMode.DropOldest });

    public ChannelReader<ChangeEvent> Reader => _channel.Reader;

    public void Publish(ChangeEvent change) => _channel.Writer.TryWrite(change);
}

// Two transports, picked by configuration:
//  - Api:RedisUrl set: XADD to the stream `taskpulse:changes` (capped) + a PUBLISH nudge. Durable and shared - an
//    event written while Realtime is restarting is delivered when it is back, and any number of Realtime nodes can
//    follow the same stream.
//  - otherwise: POST to Realtime's loopback-only internal endpoint (single box, no Redis).
public sealed class ChangeForwarder(
    ChangePublisher publisher,
    IHttpClientFactory httpClients,
    IOptions<ApiOptions> options,
    ILogger<ChangeForwarder> logger) : BackgroundService
{
    public const string HttpClientName = "realtime-internal";
    public const string StreamKey = "taskpulse:changes";
    public const string NudgeChannel = "taskpulse:changes:nudge";
    private const int StreamMaxLength = 10_000;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!string.IsNullOrWhiteSpace(settings.RedisUrl))
        {
            await ForwardThroughRedisAsync(settings.RedisUrl, stoppingToken);
            return;
        }

        if (string.IsNullOrWhiteSpace(settings.RealtimeInternalUrl))
        {
            logger.LogInformation("Change fan-out disabled (neither Api:RedisUrl nor Api:RealtimeInternalUrl is set)");
            return;
        }

        await ForwardThroughHttpAsync(settings.RealtimeInternalUrl, settings.RealtimeInternalToken, stoppingToken);
    }

    private async Task ForwardThroughRedisAsync(string redisUrl, CancellationToken stoppingToken)
    {
        var config = ConfigurationOptions.Parse(redisUrl);
        config.AbortOnConnectFail = false; // keep retrying in the background instead of failing startup
        using var redis = await ConnectionMultiplexer.ConnectAsync(config);
        var db = redis.GetDatabase();
        var subscriber = redis.GetSubscriber();
        logger.LogInformation("Change fan-out through Redis stream {Stream} ({Endpoint})", StreamKey, redisUrl);

        var failures = 0;
        await foreach (var change in publisher.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                var payload = JsonSerializer.Serialize(new { type = "changed", resource = change.Resource, action = change.Action, id = change.Id, kind = change.Kind, actor = change.Actor });
                await db.StreamAddAsync(StreamKey, [new NameValueEntry("event", payload)], maxLength: StreamMaxLength, useApproximateMaxLength: true);
                await subscriber.PublishAsync(RedisChannel.Literal(NudgeChannel), "1");
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                if (failures++ % 20 == 0)
                {
                    logger.LogWarning(e, "Redis unreachable for change events ({Endpoint})", redisUrl);
                }
            }
        }
    }

    private async Task ForwardThroughHttpAsync(string url, string? token, CancellationToken stoppingToken)
    {
        var client = httpClients.CreateClient(HttpClientName);
        if (!string.IsNullOrEmpty(token))
        {
            client.DefaultRequestHeaders.Add("X-Internal-Token", token);
        }

        var failures = 0;
        await foreach (var change in publisher.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                using var response = await client.PostAsJsonAsync(url, new { type = "changed", change.Resource, change.Action, change.Id, change.Kind, change.Actor }, stoppingToken);
                if (!response.IsSuccessStatusCode && failures++ % 20 == 0)
                {
                    logger.LogWarning("Realtime rejected a change event: {Status}", (int)response.StatusCode);
                }
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                if (failures++ % 20 == 0)
                {
                    logger.LogWarning(e, "Realtime unreachable for change events ({Url})", url);
                }
            }
        }
    }
}
