using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using TaskPulse.Api.Models;
using TaskPulse.Api.Services;
using Xunit;

namespace TaskPulse.Api.Tests;

public sealed class WebhookTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };
    private readonly HttpClient _admin = factory.CreateClient("4", "admin@techtest.dev", "Admin");
    private readonly HttpClient _user = factory.CreateClient("7", "demo@techtest.dev", "Viewer");

    // A tiny receiver on a loopback port: records what arrives, answers with the status the test wants.
    private sealed class Receiver : IDisposable
    {
        private readonly HttpListener _listener = new();
        public readonly List<(string Body, string? Signature, string? Event)> Received = [];
        public int Status = 200;
        public string Url { get; }

        public Receiver()
        {
            var port = Random.Shared.Next(20000, 40000);
            Url = $"http://127.0.0.1:{port}/hook/";
            _listener.Prefixes.Add(Url);
            _listener.Start();
            _ = Task.Run(async () =>
            {
                while (_listener.IsListening)
                {
                    HttpListenerContext ctx;
                    try
                    {
                        ctx = await _listener.GetContextAsync();
                    }
                    catch
                    {
                        return;
                    }

                    using var reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8);
                    lock (Received)
                    {
                        Received.Add((reader.ReadToEnd(), ctx.Request.Headers[WebhookDispatcher.SignatureHeader], ctx.Request.Headers[WebhookDispatcher.EventHeader]));
                    }

                    ctx.Response.StatusCode = Status;
                    ctx.Response.Close();
                }
            });
        }

        public void Dispose() => _listener.Stop();
    }

    [Fact]
    public async Task Admin_registers_a_hook_and_every_change_is_delivered_signed()
    {
        using var receiver = new Receiver();
        var secret = "webhook-test-secret-0123456789";
        Assert.Equal(HttpStatusCode.Forbidden, (await _user.PostAsJsonAsync("/api/webhooks", new { url = receiver.Url, secret })).StatusCode);
        var bad = await _admin.PostAsJsonAsync("/api/webhooks", new { url = "http://example.com/hook", secret });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode); // plain http off-loopback

        var create = await _admin.PostAsJsonAsync("/api/webhooks", new { url = receiver.Url, secret, resources = new[] { "task" }, description = "test hook" });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var hook = (await create.Content.ReadFromJsonAsync<Webhook>(Json))!;
        Assert.DoesNotContain("secret", await create.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);

        factory.Services.GetRequiredService<WebhookDispatcher>().Delays = [TimeSpan.Zero, TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(50)];
        var task = await (await _admin.PostAsJsonAsync("/api/tasks", new { title = "hooked" })).Content.ReadFromJsonAsync<TaskItem>(Json);
        await WaitFor(() => receiver.Received.Count >= 1);

        var (body, signature, evt) = receiver.Received[0];
        Assert.Equal("task.create", evt);
        Assert.Equal("sha256=" + WebhookDispatcher.Sign(secret, body), signature);
        var payload = JsonSerializer.Deserialize<JsonElement>(body);
        Assert.Equal(task!.Id.ToString(), payload.GetProperty("id").GetString());
        Assert.Equal("admin@techtest.dev", payload.GetProperty("actor").GetString());

        // A catalog change is not in this hook's resources.
        await _admin.PostAsJsonAsync("/api/catalog/hooks-kind", new { label = "Not for the hook" });
        await Task.Delay(300);
        Assert.Single(receiver.Received);

        // Failures are retried three times and logged; the hook stays on until 20 in a row.
        receiver.Status = 503;
        await _admin.PutAsJsonAsync($"/api/tasks/{task.Id}", new { title = "hooked again", status = "Done" });
        await WaitFor(() => receiver.Received.Count >= 4);
        var deliveries = await WaitForDeliveries(hook.Id, 2);
        var failed = deliveries.First(d => d.Event == "task.move" || d.Event == "task.update");
        Assert.False(failed.Delivered);
        Assert.Equal(3, failed.Attempts);
        Assert.Equal(503, failed.Status);
        var refreshed = await _admin.GetFromJsonAsync<Webhook>($"/api/webhooks/{hook.Id}", Json);
        Assert.Equal(1, refreshed!.ConsecutiveFailures);
        Assert.True(refreshed.Active);

        // Off, then no more deliveries.
        Assert.Equal(HttpStatusCode.OK, (await _admin.PutAsJsonAsync($"/api/webhooks/{hook.Id}", new { active = false })).StatusCode);
        receiver.Status = 200;
        var before = receiver.Received.Count;
        await _admin.DeleteAsync($"/api/tasks/{task.Id}");
        await Task.Delay(300);
        Assert.Equal(before, receiver.Received.Count);

        Assert.Equal(HttpStatusCode.NoContent, (await _admin.DeleteAsync($"/api/webhooks/{hook.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _admin.GetAsync($"/api/webhooks/{hook.Id}/deliveries")).StatusCode);
    }

    private async Task<List<WebhookDelivery>> WaitForDeliveries(Guid hookId, int count)
    {
        for (var i = 0; i < 40; i++)
        {
            var rows = await _admin.GetFromJsonAsync<List<WebhookDelivery>>($"/api/webhooks/{hookId}/deliveries", Json);
            if (rows!.Count >= count)
            {
                return rows;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException("deliveries did not appear");
    }

    private static async Task WaitFor(Func<bool> condition)
    {
        for (var i = 0; i < 60 && !condition(); i++)
        {
            await Task.Delay(100);
        }

        Assert.True(condition(), "timed out waiting");
    }
}
