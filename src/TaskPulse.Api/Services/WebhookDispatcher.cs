using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using TaskPulse.Api.Data;
using TaskPulse.Api.Models;

namespace TaskPulse.Api.Services;

// Outgoing webhooks: every change event the API emits is POSTed to each active hook that wants that resource, as
// JSON with an HMAC-SHA256 signature the receiver can verify. Three attempts (1 s, 4 s apart), then the delivery is
// recorded as failed; a hook that fails 20 deliveries in a row is switched off so a dead endpoint stops costing
// retries. Runs behind a bounded channel: a write never waits for a webhook.
public sealed class WebhookDispatcher(ChangePublisher publisher, IServiceScopeFactory scopes, IHttpClientFactory httpClients, TimeProvider clock, ILogger<WebhookDispatcher> logger) : BackgroundService
{
    public const string HttpClientName = "webhooks";
    public const string SignatureHeader = "X-TaskPulse-Signature";
    public const string EventHeader = "X-TaskPulse-Event";
    public const int DisableAfterFailures = 20;
    private static readonly TimeSpan[] Backoff = [TimeSpan.Zero, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(4)];
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // Visible for tests: the delays between attempts can be shortened.
    public TimeSpan[] Delays { get; set; } = Backoff;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var reader = publisher.Subscribe();
        await foreach (var change in reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await DispatchAsync(change, stoppingToken);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                logger.LogWarning(e, "Webhook dispatch failed for {Resource}/{Action} {Id}", change.Resource, change.Action, change.Id);
            }
        }
    }

    public async Task DispatchAsync(ChangeEvent change, CancellationToken cancellationToken)
    {
        if (change.Resource == "webhook")
        {
            return; // hook administration is not something hooks get told about
        }

        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TasksDbContext>();
        var hooks = await db.Webhooks.AsNoTracking().Where(h => h.Active).ToListAsync(cancellationToken);
        hooks = hooks.Where(h => h.Resources.Length == 0 || h.Resources.Contains(change.Resource)).ToList();
        if (hooks.Count == 0)
        {
            return;
        }

        var eventName = $"{change.Resource}.{change.Action}";
        var payload = JsonSerializer.Serialize(new
        {
            @event = eventName,
            at = clock.GetUtcNow(),
            resource = change.Resource,
            action = change.Action,
            id = change.Id,
            kind = change.Kind,
            actor = change.Actor,
        }, Json);

        // Every hook is sent to at the same time (a slow endpoint must not hold the others back); the bookkeeping
        // below runs one hook at a time because a DbContext is not thread-safe.
        var results = await Task.WhenAll(hooks.Select(hook => SendAsync(hook, eventName, payload, cancellationToken)));
        foreach (var (hook, (delivered, status, error, attempts)) in hooks.Zip(results))
        {
            var now = clock.GetUtcNow().UtcDateTime;
            var failures = delivered ? 0 : hook.ConsecutiveFailures + 1;
            var active = delivered || failures < DisableAfterFailures;
            if (!active)
            {
                logger.LogWarning("Webhook {Url} switched off after {Failures} failed deliveries in a row", hook.Url, failures);
            }

            // One statement per hook, so a hook deleted or edited while its retries were running (they take seconds)
            // neither fails the whole batch nor gets a delivery row after it is gone.
            var updated = await db.Webhooks.Where(h => h.Id == hook.Id).ExecuteUpdateAsync(set => set
                .SetProperty(h => h.LastAttemptAtUtc, now)
                .SetProperty(h => h.LastStatus, status)
                .SetProperty(h => h.ConsecutiveFailures, failures)
                .SetProperty(h => h.Active, h => h.Active && active), cancellationToken);
            if (updated == 0)
            {
                continue;
            }

            db.WebhookDeliveries.Add(new WebhookDeliveryEntity
            {
                WebhookId = hook.Id,
                AtUtc = now,
                Event = eventName,
                Payload = payload,
                Attempts = attempts,
                Status = status,
                Error = error is null ? null : error[..Math.Min(error.Length, 300)],
                Delivered = delivered,
            });
            await db.SaveChangesAsync(cancellationToken);

            // keep the log short: the newest DeliveriesKept per hook
            var stale = await db.WebhookDeliveries.Where(d => d.WebhookId == hook.Id).OrderByDescending(d => d.Id).Skip(WebhookLimits.DeliveriesKept).Select(d => d.Id).ToListAsync(cancellationToken);
            if (stale.Count > 0)
            {
                await db.WebhookDeliveries.Where(d => stale.Contains(d.Id)).ExecuteDeleteAsync(cancellationToken);
            }
        }
    }

    private async Task<(bool Delivered, int? Status, string? Error, int Attempts)> SendAsync(WebhookEntity hook, string eventName, string payload, CancellationToken cancellationToken)
    {
        var client = httpClients.CreateClient(HttpClientName);
        int? status = null;
        string? error = null;
        var attempts = 0;
        foreach (var delay in Delays)
        {
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, cancellationToken);
            }

            attempts++;
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, hook.Url) { Content = new StringContent(payload, Encoding.UTF8, "application/json") };
                request.Headers.Add(SignatureHeader, "sha256=" + Sign(hook.Secret, payload));
                request.Headers.Add(EventHeader, eventName);
                using var response = await client.SendAsync(request, cancellationToken);
                status = (int)response.StatusCode;
                if (response.IsSuccessStatusCode)
                {
                    return (true, status, null, attempts);
                }

                error = $"HTTP {status}";
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
            {
                error = e.Message;
                status = null;
            }
        }

        return (false, status, error, attempts);
    }

    public static string Sign(string secret, string body)
        => Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(body)));
}
