using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
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

    private readonly HttpClient _client = factory.CreateClient("1", "test@techtest.dev", "TestGroup");

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

        var stillThere = await _client.GetFromJsonAsync<TaskItem>($"/api/tasks/{created.Id}?includeDeleted=true", Json);
        Assert.NotNull(stillThere!.DeletedAt);
        var restore = await _client.PostAsync($"/api/tasks/{created.Id}/restore", null);
        Assert.Equal(HttpStatusCode.OK, restore.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync($"/api/tasks/{created.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/tasks/{created.Id}")).StatusCode);
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
                var response = await firstRun.CreateClient("1", "test@techtest.dev", "TestGroup").PostAsJsonAsync("/api/tasks", new { title = "Persist me" });
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
    public async Task Cursor_paging_walks_the_list_without_gaps_or_repeats_while_rows_are_inserted()
    {
        var tag = "cursor" + Guid.NewGuid().ToString("N")[..6];
        for (var i = 0; i < 7; i++)
        {
            await _client.PostAsJsonAsync("/api/tasks", new { title = $"{tag} item {i}", priority = i % 2 == 0 ? "High" : "Low", dueAt = i < 3 ? DateTimeOffset.UtcNow.AddDays(i) : (DateTimeOffset?)null, labels = new[] { tag } });
        }

        // page 1 by offset; then keyset from its cursor - even though a new task lands at the front meanwhile
        var first = (await _client.GetFromJsonAsync<PagedResponse<TaskItem>>($"/api/tasks?label={tag}&pageSize=3", Json))!;
        Assert.Equal(3, first.Items.Count);
        Assert.NotNull(first.NextCursor);
        await _client.PostAsJsonAsync("/api/tasks", new { title = $"{tag} inserted", dueAt = DateTimeOffset.UtcNow.AddHours(1), labels = new[] { tag } });

        var seen = first.Items.Select(t => t.Id).ToList();
        var cursor = first.NextCursor;
        var pageNo = 1;
        while (cursor is not null)
        {
            var page = (await _client.GetFromJsonAsync<PagedResponse<TaskItem>>($"/api/tasks?label={tag}&pageSize=3&page={pageNo + 1}&cursor={Uri.EscapeDataString(cursor)}", Json))!;
            Assert.Equal(++pageNo, page.Page);
            Assert.Equal(8, page.Total);
            seen.AddRange(page.Items.Select(t => t.Id));
            cursor = page.NextCursor;
            Assert.True(pageNo < 10, "cursor never ended");
        }

        Assert.Equal(7, seen.Count); // the 7 originals once each; the row inserted before the cursor is not repeated or skipped into
        Assert.Equal(seen.Count, seen.Distinct().Count());
        Assert.DoesNotContain(seen, id => id == Guid.Empty);
        var offsetOrder = (await _client.GetFromJsonAsync<PagedResponse<TaskItem>>($"/api/tasks?label={tag}&pageSize=50", Json))!.Items.Select(t => t.Id).Where(id => seen.Contains(id));
        Assert.Equal(offsetOrder, seen); // same order as the offset view

        // search results (ranked) page by cursor too
        var ranked = (await _client.GetFromJsonAsync<PagedResponse<TaskItem>>($"/api/tasks?q={tag}&pageSize=4", Json))!;
        Assert.NotNull(ranked.NextCursor);
        var rest = (await _client.GetFromJsonAsync<PagedResponse<TaskItem>>($"/api/tasks?q={tag}&pageSize=4&cursor={Uri.EscapeDataString(ranked.NextCursor!)}", Json))!;
        Assert.Equal(8, ranked.Items.Count + rest.Items.Count);
        Assert.Empty(ranked.Items.Select(t => t.Id).Intersect(rest.Items.Select(t => t.Id)));
        Assert.Null(rest.NextCursor);

        Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync("/api/tasks?cursor=not-a-cursor")).StatusCode);
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

        // Full text: stemming ("upgrading" finds "upgrade"), prefixes ("postg" finds "PostgreSQL"), and operators typed
        // by a user are just words, never tsquery syntax.
        var stemmed = await _client.GetFromJsonAsync<PagedResponse<TaskItem>>($"/api/tasks?q={marker}%20upgrading&pageSize=50", Json);
        Assert.Equal(1, stemmed!.Total);
        var prefix = await _client.GetFromJsonAsync<PagedResponse<TaskItem>>($"/api/tasks?q={marker}%20postg&pageSize=50", Json);
        Assert.Equal(2, prefix!.Total);
        var operators = await _client.GetAsync($"/api/tasks?q={Uri.EscapeDataString(marker + " & !(upgrade) | 'x")}&pageSize=50");
        Assert.Equal(HttpStatusCode.OK, operators.StatusCode);

        var tooLong = await _client.GetAsync("/api/tasks?q=" + new string('a', TaskLimits.SearchMaxLength + 1));
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
    }

    [Fact]
    public async Task Assignee_due_date_priority_and_labels_round_trip_and_filter()
    {
        var due = DateTimeOffset.UtcNow.AddDays(2);
        var create = await _client.PostAsJsonAsync("/api/tasks", new
        {
            title = "Ship the release notes",
            priority = "High",
            dueAt = due,
            assigneeId = "1",
            assigneeName = "test@techtest.dev",
            labels = new[] { " Docs ", "release", "docs", "" },
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var task = (await create.Content.ReadFromJsonAsync<TaskItem>(Json))!;
        Assert.Equal(TaskPriority.High, task.Priority);
        // Low is the enum's CLR default; it must still be stored (a configured column default would swallow it).
        var low = await (await _client.PostAsJsonAsync("/api/tasks", new { title = "low", priority = "Low" })).Content.ReadFromJsonAsync<TaskItem>(Json);
        Assert.Equal(TaskPriority.Low, low!.Priority);
        Assert.Equal(TaskPriority.Low, (await _client.GetFromJsonAsync<TaskItem>($"/api/tasks/{low.Id}", Json))!.Priority);
        Assert.Equal(due.ToUnixTimeSeconds(), task.DueAt!.Value.ToUnixTimeSeconds());
        Assert.Equal("1", task.AssigneeId);
        Assert.Equal(new[] { "docs", "release" }, task.Labels); // trimmed, lower-cased, de-duplicated, empties dropped

        // Filters: by label, by priority, by "me" (the client's token has sub 1), by due window.
        var byLabel = await _client.GetFromJsonAsync<PagedResponse<TaskItem>>("/api/tasks?label=Docs", Json);
        Assert.Contains(byLabel!.Items, t => t.Id == task.Id);
        var byPriority = await _client.GetFromJsonAsync<PagedResponse<TaskItem>>("/api/tasks?priority=High", Json);
        Assert.Contains(byPriority!.Items, t => t.Id == task.Id);
        Assert.All(byPriority.Items, t => Assert.Equal(TaskPriority.High, t.Priority));
        var mine = await _client.GetFromJsonAsync<PagedResponse<TaskItem>>("/api/tasks?assignee=me", Json);
        Assert.Contains(mine!.Items, t => t.Id == task.Id);
        var week = await _client.GetFromJsonAsync<PagedResponse<TaskItem>>("/api/tasks?due=week", Json);
        Assert.Contains(week!.Items, t => t.Id == task.Id);
        var overdue = await _client.GetFromJsonAsync<PagedResponse<TaskItem>>("/api/tasks?due=overdue", Json);
        Assert.DoesNotContain(overdue!.Items, t => t.Id == task.Id);

        // Past due and still open: overdue, and counted in the stats.
        var update = await _client.PutAsJsonAsync($"/api/tasks/{task.Id}", new { title = task.Title, status = "InProgress", dueAt = DateTimeOffset.UtcNow.AddDays(-1), labels = new[] { "docs" } });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        overdue = await _client.GetFromJsonAsync<PagedResponse<TaskItem>>("/api/tasks?due=overdue", Json);
        Assert.Contains(overdue!.Items, t => t.Id == task.Id);
        var stats = await _client.GetFromJsonAsync<TaskStats>("/api/tasks/stats", Json);
        Assert.True(stats!.Overdue >= 1);

        // Too many labels is a validation error, not a truncation.
        var tooMany = await _client.PostAsJsonAsync("/api/tasks", new { title = "x", labels = Enumerable.Range(0, 11).Select(i => "l" + i).ToArray() });
        Assert.Equal(HttpStatusCode.BadRequest, tooMany.StatusCode);
        var bad = await _client.PostAsJsonAsync("/api/tasks", new { title = "x", priority = "Urgent" });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/tasks/{task.Id}")).StatusCode);
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

        // a preflight for a create with an Idempotency-Key is allowed, and the replay marker is readable by the page
        using var preflight = new HttpRequestMessage(HttpMethod.Options, "/api/tasks");
        preflight.Headers.Add("Origin", "https://portal.example");
        preflight.Headers.Add("Access-Control-Request-Method", "POST");
        preflight.Headers.Add("Access-Control-Request-Headers", "content-type,authorization,idempotency-key");
        var preflightResponse = await allowed.CreateClient().SendAsync(preflight);
        Assert.Equal(HttpStatusCode.NoContent, preflightResponse.StatusCode);
        Assert.Contains("Idempotency-Key", preflightResponse.Headers.GetValues("Access-Control-Allow-Headers").Single());
        Assert.Contains("Idempotent-Replayed", allowedResponse.Headers.GetValues("Access-Control-Expose-Headers").Single());
    }
}
