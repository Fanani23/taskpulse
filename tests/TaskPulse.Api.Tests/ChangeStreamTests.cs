using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using StackExchange.Redis;
using TaskPulse.Api.Services;
using Xunit;

namespace TaskPulse.Api.Tests;

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

public sealed class ChangeStreamTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [RedisFact]
    public async Task Every_write_lands_in_the_redis_stream_with_the_actor()
    {
        var redisUrl = Environment.GetEnvironmentVariable("TASKPULSE_TEST_REDIS")!;
        using var redis = await ConnectionMultiplexer.ConnectAsync(redisUrl);
        var db = redis.GetDatabase();
        var before = await db.StreamRangeAsync(ChangeForwarder.StreamKey, "-", "+", count: 1, messageOrder: Order.Descending);
        var since = before.Length > 0 ? before[0].Id : (RedisValue)"0-0";

        using var app = factory.WithWebHostBuilder(b => b.UseSetting("Api:RedisUrl", redisUrl));
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", TestTokens.Issue("42", "stream@techtest.dev", ["User"]));
        var title = "streamed " + Guid.NewGuid().ToString("N")[..8];
        var response = await client.PostAsJsonAsync("/api/tasks", new { title });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var id = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString();

        // The forwarder is fire-and-forget; give it a moment, then read everything written since.
        JsonElement? found = null;
        for (var i = 0; i < 40 && found is null; i++)
        {
            await Task.Delay(100);
            foreach (var entry in await db.StreamReadAsync(ChangeForwarder.StreamKey, since))
            {
                var json = (string?)entry.Values.First(v => v.Name == "event").Value;
                var e = JsonSerializer.Deserialize<JsonElement>(json!);
                if (e.GetProperty("id").GetString() == id)
                {
                    found = e;
                }
            }
        }

        Assert.NotNull(found);
        Assert.Equal("changed", found.Value.GetProperty("type").GetString());
        Assert.Equal("task", found.Value.GetProperty("resource").GetString());
        Assert.Equal("create", found.Value.GetProperty("action").GetString());
        Assert.Equal("stream@techtest.dev", found.Value.GetProperty("actor").GetString());
    }
}
