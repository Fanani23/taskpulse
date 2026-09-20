using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace TaskPulse.Realtime.Tests;

public sealed class WebSocketEndpointTests(RealtimeFactory factory) : IClassFixture<RealtimeFactory>
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Connect_receives_welcome_then_echo_and_pong()
    {
        using var socket = await ConnectAsync();

        var welcome = await ReceiveAsync(socket);
        Assert.Equal("welcome", welcome.GetProperty("type").GetString());
        var connectionId = welcome.GetProperty("connectionId").GetString();
        Assert.False(string.IsNullOrEmpty(connectionId));

        await SendAsync(socket, """{"type":"echo","data":"hello"}""");
        var echo = await ReceiveAsync(socket);
        Assert.Equal("echo", echo.GetProperty("type").GetString());
        Assert.Equal("hello", echo.GetProperty("data").GetString());
        Assert.Equal(connectionId, echo.GetProperty("from").GetString());

        await SendAsync(socket, """{"type":"ping"}""");
        var pong = await ReceiveAsync(socket);
        Assert.Equal("pong", pong.GetProperty("type").GetString());

        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
    }

    [Fact]
    public async Task Broadcast_needs_a_signed_in_connection_and_reaches_every_connection()
    {
        using var alice = await ConnectAsync();
        _ = await ReceiveAsync(alice);

        using var bob = await ConnectAsync();
        _ = await ReceiveAsync(bob);
        var joined = await ReceiveAsync(alice);
        Assert.Equal("system", joined.GetProperty("type").GetString());
        Assert.Equal("joined", joined.GetProperty("event").GetString());

        // Anonymous: refused, nobody else hears it.
        await SendAsync(alice, """{"type":"broadcast","data":"to everyone"}""");
        var refused = await ReceiveAsync(alice);
        Assert.Equal("error", refused.GetProperty("type").GetString());
        Assert.Contains("Sign in", refused.GetProperty("error").GetString());

        // A bad token is refused too.
        await SendAsync(alice, """{"type":"auth","token":"not.a.token"}""");
        Assert.Equal("Invalid or expired token.", (await ReceiveAsync(alice)).GetProperty("error").GetString());
        await SendAsync(alice, $$"""{"type":"auth","token":"{{RealtimeFactory.IssueToken("7", "expired@techtest.dev", ["User"], TimeSpan.FromMinutes(-5))}}"}""");
        Assert.Equal("Invalid or expired token.", (await ReceiveAsync(alice)).GetProperty("error").GetString());

        // The access token from the Vue + Express sign-in attaches the identity; the broadcast names it.
        await SendAsync(alice, $$"""{"type":"auth","token":"{{RealtimeFactory.IssueToken("7", "alice@techtest.dev", ["User"])}}"}""");
        var authed = await ReceiveAsync(alice);
        Assert.Equal("authed", authed.GetProperty("type").GetString());
        Assert.Equal("alice@techtest.dev", authed.GetProperty("user").GetString());

        await SendAsync(alice, """{"type":"broadcast","data":"to everyone"}""");

        var seenByAlice = await ReceiveAsync(alice);
        var seenByBob = await ReceiveAsync(bob);
        Assert.Equal("broadcast", seenByAlice.GetProperty("type").GetString());
        Assert.Equal("to everyone", seenByBob.GetProperty("data").GetString());
        Assert.Equal("alice@techtest.dev", seenByBob.GetProperty("actor").GetString());

        var stats = await factory.CreateClient().GetFromJsonAsync<JsonElement>("/stats");
        Assert.Contains(stats.GetProperty("clients").EnumerateArray(), c => c.TryGetProperty("user", out var u) && u.GetString() == "alice@techtest.dev");

        await alice.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
        await bob.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
    }

    [Fact]
    public async Task Rate_limits_answer_with_an_error_then_close_the_connection()
    {
        using var socket = await ConnectAsync();
        _ = await ReceiveAsync(socket);
        await SendAsync(socket, $$"""{"type":"auth","token":"{{RealtimeFactory.IssueToken("9", "spam@techtest.dev", ["User"])}}"}""");
        Assert.Equal("authed", (await ReceiveAsync(socket)).GetProperty("type").GetString());

        // Broadcasts: the limit is honoured per connection, the message past it is an error, the socket stays open.
        for (var i = 0; i < RealtimeFactory.BroadcastsPerMinute; i++)
        {
            await SendAsync(socket, """{"type":"broadcast","data":"x"}""");
            Assert.Equal("broadcast", (await ReceiveAsync(socket)).GetProperty("type").GetString());
        }

        await SendAsync(socket, """{"type":"broadcast","data":"one too many"}""");
        var limited = await ReceiveAsync(socket);
        Assert.Equal("error", limited.GetProperty("type").GetString());
        Assert.Contains("Too many broadcasts", limited.GetProperty("error").GetString());

        // Messages of any kind: past the limit every message is an error, past twice the limit the server closes 1008.
        var sent = 1 + RealtimeFactory.BroadcastsPerMinute + 1;
        for (; sent < RealtimeFactory.MessagesPerMinute; sent++)
        {
            await SendAsync(socket, """{"type":"ping"}""");
            Assert.Equal("pong", (await ReceiveAsync(socket)).GetProperty("type").GetString());
        }

        await SendAsync(socket, """{"type":"ping"}""");
        Assert.Contains("Too many messages", (await ReceiveAsync(socket)).GetProperty("error").GetString());

        for (sent++; sent < RealtimeFactory.MessagesPerMinute * 2; sent++)
        {
            await SendAsync(socket, """{"type":"ping"}""");
            _ = await ReceiveAsync(socket);
        }

        await SendAsync(socket, """{"type":"ping"}""");
        using var cts = new CancellationTokenSource(Timeout);
        var close = await socket.ReceiveAsync(new byte[1024], cts.Token);
        Assert.Equal(WebSocketMessageType.Close, close.MessageType);
        Assert.Equal(WebSocketCloseStatus.PolicyViolation, close.CloseStatus);
    }

    [Fact]
    public async Task Raw_text_is_echoed_and_bad_json_returns_error()
    {
        using var socket = await ConnectAsync();
        _ = await ReceiveAsync(socket);

        await SendAsync(socket, "plain text");
        var echo = await ReceiveAsync(socket);
        Assert.Equal("echo", echo.GetProperty("type").GetString());
        Assert.Equal("plain text", echo.GetProperty("data").GetString());

        await SendAsync(socket, "{not json");
        var error = await ReceiveAsync(socket);
        Assert.Equal("error", error.GetProperty("type").GetString());

        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
    }

    [Fact]
    public async Task Plain_http_get_on_ws_route_is_rejected()
    {
        var response = await factory.CreateClient().GetAsync("/ws");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Stats_and_health_are_served()
    {
        var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);

        var stats = await client.GetFromJsonAsync<JsonElement>("/stats");
        Assert.True(stats.TryGetProperty("connections", out _));
    }

    [Fact]
    public async Task Internal_broadcast_fans_out_change_events_and_is_loopback_only()
    {
        using var socket = await ConnectAsync();
        await ReceiveAsync(socket);

        var client = factory.CreateClient();
        var accepted = await client.PostAsJsonAsync("/internal/broadcast", new { resource = "task", action = "create", id = "abc", kind = (string?)null, actor = "demo@techtest.dev" });
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);

        var changed = await ReceiveAsync(socket);
        Assert.Equal("changed", changed.GetProperty("type").GetString());
        Assert.Equal("task", changed.GetProperty("resource").GetString());
        Assert.Equal("create", changed.GetProperty("action").GetString());
        Assert.Equal("abc", changed.GetProperty("id").GetString());
        Assert.Equal("demo@techtest.dev", changed.GetProperty("actor").GetString());

        using var proxied = new HttpRequestMessage(HttpMethod.Post, "/internal/broadcast") { Content = JsonContent.Create(new { resource = "task", action = "create", id = "x" }) };
        proxied.Headers.Add("X-Forwarded-For", "203.0.113.9");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(proxied)).StatusCode);

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/internal/broadcast", new { resource = "", action = "", id = "" })).StatusCode);
    }

    private async Task<WebSocket> ConnectAsync()
    {
        var client = factory.Server.CreateWebSocketClient();
        var uri = new UriBuilder(factory.Server.BaseAddress) { Scheme = "ws", Path = "/ws" }.Uri;
        using var cts = new CancellationTokenSource(Timeout);
        return await client.ConnectAsync(uri, cts.Token);
    }

    private static async Task SendAsync(WebSocket socket, string text)
    {
        using var cts = new CancellationTokenSource(Timeout);
        await socket.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, endOfMessage: true, cts.Token);
    }

    private static async Task<JsonElement> ReceiveAsync(WebSocket socket)
    {
        using var cts = new CancellationTokenSource(Timeout);
        var buffer = new byte[16 * 1024];
        var result = await socket.ReceiveAsync(buffer, cts.Token);
        Assert.Equal(WebSocketMessageType.Text, result.MessageType);
        Assert.True(result.EndOfMessage);
        return JsonSerializer.Deserialize<JsonElement>(buffer.AsSpan(0, result.Count));
    }
}
