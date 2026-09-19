using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace TaskPulse.Realtime.Tests;

public sealed class WebSocketEndpointTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
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
    public async Task Broadcast_reaches_every_connection()
    {
        using var alice = await ConnectAsync();
        _ = await ReceiveAsync(alice);

        using var bob = await ConnectAsync();
        _ = await ReceiveAsync(bob);
        var joined = await ReceiveAsync(alice);
        Assert.Equal("system", joined.GetProperty("type").GetString());
        Assert.Equal("joined", joined.GetProperty("event").GetString());

        await SendAsync(alice, """{"type":"broadcast","data":"to everyone"}""");

        var seenByAlice = await ReceiveAsync(alice);
        var seenByBob = await ReceiveAsync(bob);
        Assert.Equal("broadcast", seenByAlice.GetProperty("type").GetString());
        Assert.Equal("to everyone", seenByBob.GetProperty("data").GetString());

        await alice.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
        await bob.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
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
