using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using StackExchange.Redis;
using TaskPulse.Realtime.Services;
using Xunit;

namespace TaskPulse.Realtime.Tests;

// Runs only when a Redis is reachable (TASKPULSE_TEST_REDIS, e.g. "127.0.0.1:6379"); skipped otherwise.
public sealed class RedisFactAttribute : FactAttribute
{
    public RedisFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TASKPULSE_TEST_REDIS")))
        {
            Skip = "TASKPULSE_TEST_REDIS not set";
        }
    }
}

public sealed class ChangeStreamTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [RedisFact]
    public async Task Stream_entries_written_by_the_api_reach_every_socket_and_the_cursor_survives()
    {
        var redisUrl = Environment.GetEnvironmentVariable("TASKPULSE_TEST_REDIS")!;
        var node = "test-" + Guid.NewGuid().ToString("N")[..8];
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("WebSocket:RedisUrl", redisUrl);
            b.UseSetting("WebSocket:NodeName", node);
        });

        using var socket = await ConnectAsync(factory);
        _ = await ReceiveAsync(socket); // welcome
        await Task.Delay(500); // let the reader take its position at the end of the stream

        using var redis = await ConnectionMultiplexer.ConnectAsync(redisUrl);
        var db = redis.GetDatabase();
        var payload = JsonSerializer.Serialize(new { type = "changed", resource = "task", action = "create", id = node, kind = (string?)null, actor = "api@techtest.dev" });
        var entryId = await db.StreamAddAsync(ChangeStreamReader.StreamKey, [new NameValueEntry("event", payload)], maxLength: 10_000, useApproximateMaxLength: true);
        await redis.GetSubscriber().PublishAsync(RedisChannel.Literal(ChangeStreamReader.NudgeChannel), "1");

        var changed = await ReceiveAsync(socket);
        Assert.Equal("changed", changed.GetProperty("type").GetString());
        Assert.Equal(node, changed.GetProperty("id").GetString());
        Assert.Equal("api@techtest.dev", changed.GetProperty("actor").GetString());

        // The node remembers where it is, so a restart resumes from here instead of the end of the stream.
        await Task.Delay(300);
        var cursor = await db.StringGetAsync($"taskpulse:realtime:cursor:{node}");
        Assert.Equal(entryId, cursor);
        await db.KeyDeleteAsync($"taskpulse:realtime:cursor:{node}");

        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
    }

    [RedisFact]
    public async Task A_broadcast_on_one_node_reaches_the_sockets_of_another_node_once()
    {
        var redisUrl = Environment.GetEnvironmentVariable("TASKPULSE_TEST_REDIS")!;
        WebApplicationFactory<Program> Node(string name) => new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("WebSocket:RedisUrl", redisUrl);
            b.UseSetting("WebSocket:NodeName", name);
            b.UseSetting("WebSocket:JwtSecret", RealtimeFactory.JwtSecret);
        });
        await using var nodeA = Node("node-a-" + Guid.NewGuid().ToString("N")[..6]);
        await using var nodeB = Node("node-b-" + Guid.NewGuid().ToString("N")[..6]);

        using var alice = await ConnectAsync(nodeA);
        using var bob = await ConnectAsync(nodeB);
        _ = await ReceiveAsync(alice);
        _ = await ReceiveAsync(bob);
        await Task.Delay(500); // both node buses subscribed

        await SendAsync(alice, $$"""{"type":"auth","token":"{{RealtimeFactory.IssueToken("7", "alice@techtest.dev", ["User"])}}"}""");
        Assert.Equal("authed", (await ReceiveAsync(alice)).GetProperty("type").GetString());
        await SendAsync(alice, """{"type":"broadcast","data":"hello from node A"}""");

        // Bob, on the other node, gets it through Redis - with the sender's actor; Alice gets it once, not twice.
        var onB = await ReceiveAsync(bob);
        Assert.Equal("broadcast", onB.GetProperty("type").GetString());
        Assert.Equal("hello from node A", onB.GetProperty("data").GetString());
        Assert.Equal("alice@techtest.dev", onB.GetProperty("actor").GetString());
        var onA = await ReceiveAsync(alice);
        Assert.Equal("hello from node A", onA.GetProperty("data").GetString());
        using (var cts = new CancellationTokenSource(700))
        {
            var buffer = new byte[1024];
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => alice.ReceiveAsync(buffer, cts.Token));
        }

        var stats = await nodeB.CreateClient().GetFromJsonAsync<JsonElement>("/stats");
        Assert.True(stats.GetProperty("redis").GetBoolean());
        Assert.Equal(1, stats.GetProperty("relayedBroadcasts").GetInt64());
        Assert.StartsWith("node-b-", stats.GetProperty("node").GetString());

        await alice.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
        await bob.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
    }

    private static async Task SendAsync(WebSocket socket, string json)
    {
        using var cts = new CancellationTokenSource(Timeout);
        await socket.SendAsync(System.Text.Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, true, cts.Token);
    }

    private static async Task<WebSocket> ConnectAsync(WebApplicationFactory<Program> factory)
    {
        var client = factory.Server.CreateWebSocketClient();
        var uri = new UriBuilder(factory.Server.BaseAddress) { Scheme = "ws", Path = "/ws" }.Uri;
        using var cts = new CancellationTokenSource(Timeout);
        return await client.ConnectAsync(uri, cts.Token);
    }

    private static async Task<JsonElement> ReceiveAsync(WebSocket socket)
    {
        using var cts = new CancellationTokenSource(Timeout);
        var buffer = new byte[16 * 1024];
        var result = await socket.ReceiveAsync(buffer, cts.Token);
        Assert.Equal(WebSocketMessageType.Text, result.MessageType);
        return JsonSerializer.Deserialize<JsonElement>(buffer.AsSpan(0, result.Count));
    }
}
