using System.Text.Json;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using TaskPulse.Realtime.Infrastructure;
using TaskPulse.Realtime.Models;

namespace TaskPulse.Realtime.Services;

// Follows the API's change stream in Redis (`taskpulse:changes`) and fans every entry out as a `changed` message.
// The cursor (last stream id seen) is kept in Redis per node, so a restart resumes where it stopped instead of
// losing the events written meanwhile; a brand-new node starts at the end. A PUBLISH on the nudge channel wakes the
// loop at once; otherwise it polls every second, so a missed nudge costs at most that.
public sealed class ChangeStreamReader(ConnectionManager connections, IOptions<WsOptions> options, ILogger<ChangeStreamReader> logger) : BackgroundService
{
    public const string StreamKey = "taskpulse:changes";
    public const string NudgeChannel = "taskpulse:changes:nudge";
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // Visible for tests and /stats: how many stream entries this node has fanned out.
    public long Delivered { get; private set; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var redisUrl = options.Value.RedisUrl;
        if (string.IsNullOrWhiteSpace(redisUrl))
        {
            return;
        }

        var cursorKey = $"taskpulse:realtime:cursor:{options.Value.NodeName ?? Environment.MachineName}";
        var nudged = new SemaphoreSlim(0, 1);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var config = ConfigurationOptions.Parse(redisUrl);
                config.AbortOnConnectFail = false;
                using var redis = await ConnectionMultiplexer.ConnectAsync(config);
                var db = redis.GetDatabase();
                await redis.GetSubscriber().SubscribeAsync(RedisChannel.Literal(NudgeChannel), (_, _) => { if (nudged.CurrentCount == 0) { nudged.Release(); } });

                var cursor = await db.StringGetAsync(cursorKey);
                var position = cursor.HasValue ? (RedisValue)cursor : await LatestIdAsync(db);
                logger.LogInformation("Following change stream {Stream} from {Position} ({Endpoint})", StreamKey, position, redisUrl);

                while (!stoppingToken.IsCancellationRequested)
                {
                    var entries = await db.StreamReadAsync(StreamKey, position, count: 100);
                    foreach (var entry in entries)
                    {
                        await FanOutAsync(entry, stoppingToken);
                        position = entry.Id;
                    }

                    if (entries.Length > 0)
                    {
                        await db.StringSetAsync(cursorKey, position);
                        continue; // there may be more
                    }

                    await nudged.WaitAsync(PollInterval, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e)
            {
                logger.LogWarning(e, "Change stream reader lost Redis ({Endpoint}); retrying in 5 s", redisUrl);
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

    private static async Task<RedisValue> LatestIdAsync(IDatabase db)
    {
        var last = await db.StreamRangeAsync(StreamKey, "-", "+", count: 1, messageOrder: Order.Descending);
        return last.Length > 0 ? last[0].Id : (RedisValue)"0-0";
    }

    private async Task FanOutAsync(StreamEntry entry, CancellationToken cancellationToken)
    {
        var json = entry.Values.FirstOrDefault(v => v.Name == "event").Value;
        if (!json.HasValue)
        {
            return;
        }

        ChangeNotification? change;
        try
        {
            change = JsonSerializer.Deserialize<ChangeNotification>((string)json!, Json);
        }
        catch (JsonException e)
        {
            logger.LogWarning(e, "Skipping malformed change event {Id}", entry.Id);
            return;
        }

        if (change is null || string.IsNullOrEmpty(change.Resource) || string.IsNullOrEmpty(change.Action) || string.IsNullOrEmpty(change.Id))
        {
            return;
        }

        Delivered++;
        await connections.BroadcastAsync(ServerMessage.Changed(change), exceptId: null, cancellationToken);
    }
}
