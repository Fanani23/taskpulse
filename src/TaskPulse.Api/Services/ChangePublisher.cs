using System.Net.Http.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Options;
using TaskPulse.Api.Infrastructure;

namespace TaskPulse.Api.Services;

public sealed record ChangeEvent(string Resource, string Action, string Id, string? Kind, string? Actor);

public interface IChangePublisher
{
    void Publish(ChangeEvent change);
}

// Fan-out of "something changed" to TaskPulse.Realtime over its loopback-only internal endpoint. Fire-and-forget:
// a write must never fail or slow down because the socket server is down, so events go through a bounded channel
// drained by a background service; on overflow the oldest event is dropped (clients re-read state anyway).
public sealed class ChangePublisher : IChangePublisher
{
    private readonly Channel<ChangeEvent> _channel = Channel.CreateBounded<ChangeEvent>(new BoundedChannelOptions(256) { FullMode = BoundedChannelFullMode.DropOldest });

    public ChannelReader<ChangeEvent> Reader => _channel.Reader;

    public void Publish(ChangeEvent change) => _channel.Writer.TryWrite(change);
}

public sealed class ChangeForwarder(
    ChangePublisher publisher,
    IHttpClientFactory httpClients,
    IOptions<ApiOptions> options,
    ILogger<ChangeForwarder> logger) : BackgroundService
{
    public const string HttpClientName = "realtime-internal";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var url = options.Value.RealtimeInternalUrl;
        if (string.IsNullOrWhiteSpace(url))
        {
            logger.LogInformation("Change fan-out disabled (Api:RealtimeInternalUrl not set)");
            return;
        }

        var client = httpClients.CreateClient(HttpClientName);
        if (!string.IsNullOrEmpty(options.Value.RealtimeInternalToken))
        {
            client.DefaultRequestHeaders.Add("X-Internal-Token", options.Value.RealtimeInternalToken);
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
