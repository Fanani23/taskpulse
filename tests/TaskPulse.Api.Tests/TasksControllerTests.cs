using Microsoft.AspNetCore.Hosting;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using TaskPulse.Api.Infrastructure;
using TaskPulse.Api.Models;
using Xunit;

namespace TaskPulse.Api.Tests;

public sealed class TasksControllerTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Crud_round_trip()
    {
        var create = await _client.PostAsJsonAsync("/api/tasks", new { title = "  Write tests  ", description = "xunit + TestServer" });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);

        var created = await create.Content.ReadFromJsonAsync<TaskItem>(Json);
        Assert.NotNull(created);
        Assert.Equal("Write tests", created.Title);
        Assert.Equal(TaskItemStatus.Todo, created.Status);
        Assert.Equal($"/api/tasks/{created.Id}", create.Headers.Location?.ToString());

        var fetched = await _client.GetFromJsonAsync<TaskItem>($"/api/tasks/{created.Id}", Json);
        Assert.Equal(created, fetched);

        var page = await _client.GetFromJsonAsync<PagedResponse<TaskItem>>("/api/tasks?status=Todo&pageSize=50", Json);
        Assert.NotNull(page);
        Assert.Contains(page.Items, item => item.Id == created.Id);
        Assert.All(page.Items, item => Assert.Equal(TaskItemStatus.Todo, item.Status));
        Assert.Equal(50, page.PageSize);

        var update = await _client.PutAsJsonAsync($"/api/tasks/{created.Id}", new { title = "Write tests", description = (string?)null, status = "Done" });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var updated = await update.Content.ReadFromJsonAsync<TaskItem>(Json);
        Assert.NotNull(updated);
        Assert.Equal(TaskItemStatus.Done, updated.Status);
        Assert.Null(updated.Description);
        Assert.True(updated.UpdatedAt >= created.UpdatedAt);

        var reread = await _client.GetFromJsonAsync<TaskItem>($"/api/tasks/{created.Id}", Json);
        Assert.Equal(updated, reread);

        var delete = await _client.DeleteAsync($"/api/tasks/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        var gone = await _client.GetAsync($"/api/tasks/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
    }

    [Fact]
    public async Task Data_survives_a_process_restart()
    {
        var database = $"taskpulse_restart_{Guid.NewGuid():N}";
        try
        {
            Guid id;
            using (var firstRun = ApiFactory.ForDatabase(database))
            {
                var response = await firstRun.CreateClient().PostAsJsonAsync("/api/tasks", new { title = "Persist me" });
                Assert.Equal(HttpStatusCode.Created, response.StatusCode);
                id = (await response.Content.ReadFromJsonAsync<TaskItem>(Json))!.Id;
            }

            using var secondRun = ApiFactory.ForDatabase(database);
            var fetched = await secondRun.CreateClient().GetFromJsonAsync<TaskItem>($"/api/tasks/{id}", Json);
            Assert.NotNull(fetched);
            Assert.Equal("Persist me", fetched.Title);
        }
        finally
        {
            ApiFactory.DropDatabase(database);
        }
    }

    [Fact]
    public async Task Concurrent_updates_do_not_silently_overwrite()
    {
        var create = await _client.PostAsJsonAsync("/api/tasks", new { title = "Contended" });
        var created = (await create.Content.ReadFromJsonAsync<TaskItem>(Json))!;

        var first = await _client.PutAsJsonAsync($"/api/tasks/{created.Id}", new { title = "Contended", status = "InProgress" });
        var second = await _client.PutAsJsonAsync($"/api/tasks/{created.Id}", new { title = "Contended", status = "Done" });

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        var final = await _client.GetFromJsonAsync<TaskItem>($"/api/tasks/{created.Id}", Json);
        Assert.Equal(TaskItemStatus.Done, final!.Status);
    }

    [Fact]
    public async Task Create_without_title_returns_validation_problem()
    {
        var response = await _client.PostAsJsonAsync("/api/tasks", new { title = "   ", description = "no title" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("errors").TryGetProperty("title", out _));
    }

    [Fact]
    public async Task Update_with_unknown_status_returns_bad_request()
    {
        var response = await _client.PutAsJsonAsync($"/api/tasks/{Guid.NewGuid()}", new { title = "x", status = "NotAStatus" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Unknown_id_returns_404()
    {
        var response = await _client.GetAsync($"/api/tasks/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Page_size_is_clamped_to_configured_maximum()
    {
        var page = await _client.GetFromJsonAsync<PagedResponse<TaskItem>>("/api/tasks?pageSize=99999", Json);
        Assert.NotNull(page);
        Assert.Equal(100, page.PageSize);
    }

    [Fact]
    public async Task Health_endpoints_report_healthy()
    {
        var live = await _client.GetAsync("/health");
        var ready = await _client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        Assert.Equal("Healthy", await ready.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Correlation_id_is_generated_or_echoed()
    {
        var generated = await _client.GetAsync("/health");
        Assert.True(generated.Headers.Contains(CorrelationIdMiddleware.HeaderName));

        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add(CorrelationIdMiddleware.HeaderName, "abc-123");
        var echoed = await _client.SendAsync(request);
        Assert.Equal("abc-123", echoed.Headers.GetValues(CorrelationIdMiddleware.HeaderName).Single());
    }

    [Fact]
    public async Task OpenApi_document_is_served()
    {
        var response = await _client.GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var doc = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(doc.GetProperty("paths").TryGetProperty("/api/tasks", out _));
    }
    [Fact]
    public async Task Search_matches_title_and_description_case_insensitively()
    {
        var marker = Guid.NewGuid().ToString("N")[..8];
        await _client.PostAsJsonAsync("/api/tasks", new { title = $"{marker} PostgreSQL upgrade" });
        await _client.PostAsJsonAsync("/api/tasks", new { title = $"Unrelated {marker}", description = $"mentions {marker} postgresql in the body" });
        await _client.PostAsJsonAsync("/api/tasks", new { title = $"Nothing here {marker}" });

        var page = await _client.GetFromJsonAsync<PagedResponse<TaskItem>>($"/api/tasks?q={marker}%20POSTGRES&pageSize=50", Json);
        Assert.NotNull(page);
        Assert.Equal(2, page.Total);
        Assert.All(page.Items, item => Assert.Contains(marker, item.Title + item.Description));

        var escaped = await _client.GetFromJsonAsync<PagedResponse<TaskItem>>("/api/tasks?q=%25&pageSize=1", Json);
        Assert.Equal(0, escaped!.Total);

        var tooLong = await _client.GetAsync("/api/tasks?q=" + new string('a', TaskLimits.SearchMaxLength + 1));
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
    }

    [Fact]
    public async Task Stats_reflect_status_counts_and_daily_activity()
    {
        var open = await (await _client.PostAsJsonAsync("/api/tasks", new { title = "Stats open" })).Content.ReadFromJsonAsync<TaskItem>(Json);
        var done = await (await _client.PostAsJsonAsync("/api/tasks", new { title = "Stats done" })).Content.ReadFromJsonAsync<TaskItem>(Json);
        await _client.PutAsJsonAsync($"/api/tasks/{done!.Id}", new { title = "Stats done", status = "Done" });

        var stats = await _client.GetFromJsonAsync<TaskStats>("/api/tasks/stats?days=7", Json);
        Assert.NotNull(stats);
        Assert.Equal(7, stats.Daily.Count);
        Assert.Equal(stats.Total, stats.ByStatus.Values.Sum());
        Assert.True(stats.ByStatus[TaskItemStatus.Done] >= 1);
        Assert.True(stats.CreatedToday >= 2);
        Assert.True(stats.DoneThisWeek >= 1);
        Assert.Equal(DateOnly.FromDateTime(DateTime.UtcNow), stats.Daily[^1].Date);
        Assert.True(stats.Daily[^1].Created >= 2);
        Assert.InRange(stats.CompletionRate, 0, 1);
        Assert.NotNull(stats.OldestOpen);
        Assert.Contains(stats.RecentlyUpdated, t => t.Id == done.Id);
        Assert.Equal(stats.RecentlyUpdated.OrderByDescending(t => t.UpdatedAt).Select(t => t.Id), stats.RecentlyUpdated.Select(t => t.Id));
        Assert.NotEqual(TaskItemStatus.Done, stats.OldestOpen.Status);
        Assert.True(stats.OldestOpen.CreatedAt <= open!.CreatedAt);

        var defaults = await _client.GetFromJsonAsync<TaskStats>("/api/tasks/stats", Json);
        Assert.Equal(TaskLimits.StatsDefaultDays, defaults!.Daily.Count);

        Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync("/api/tasks/stats?days=0")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync("/api/tasks/stats?days=91")).StatusCode);
    }

    [Fact]
    public async Task Cross_origin_requests_are_refused_unless_the_origin_is_allowed()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/tasks");
        request.Headers.Add("Origin", "https://evil.example");
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));

        using var allowed = ApiFactory.ForDatabase(factory.DatabaseName)
            .WithWebHostBuilder(b => b.UseSetting("Api:AllowedOrigins:0", "https://portal.example"));
        using var allowedRequest = new HttpRequestMessage(HttpMethod.Get, "/api/tasks");
        allowedRequest.Headers.Add("Origin", "https://portal.example");
        var allowedResponse = await allowed.CreateClient().SendAsync(allowedRequest);
        Assert.Equal("https://portal.example", allowedResponse.Headers.GetValues("Access-Control-Allow-Origin").Single());
    }
}
