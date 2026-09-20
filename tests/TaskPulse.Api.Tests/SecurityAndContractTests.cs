using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using TaskPulse.Api.Models;
using Xunit;

namespace TaskPulse.Api.Tests;

public sealed class SecurityAndContractTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };

    private readonly HttpClient _anonymous = factory.CreateClient();
    private readonly HttpClient _user = factory.CreateClient("7", "demo@techtest.dev", "TestGroup", "Viewer");
    private readonly HttpClient _admin = factory.CreateClient("4", "admin@techtest.dev", "Admin");

    [Fact]
    public async Task Writes_need_a_valid_bearer_and_reads_do_not()
    {
        Assert.Equal(HttpStatusCode.OK, (await _anonymous.GetAsync("/api/tasks")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _anonymous.GetAsync("/api/catalog")).StatusCode);

        var anonymousWrite = await _anonymous.PostAsJsonAsync("/api/tasks", new { title = "nope" });
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousWrite.StatusCode);
        Assert.Equal("application/problem+json", anonymousWrite.Content.Headers.ContentType?.MediaType);

        using var forged = factory.CreateClient();
        forged.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Issue("1", "x@y", ["Admin"], secret: "another-secret-that-is-long-enough"));
        Assert.Equal(HttpStatusCode.Unauthorized, (await forged.PostAsJsonAsync("/api/tasks", new { title = "nope" })).StatusCode);

        using var expired = factory.CreateClient();
        expired.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Issue("1", "x@y", [], lifetime: TimeSpan.FromMinutes(-10)));
        Assert.Equal(HttpStatusCode.Unauthorized, (await expired.PostAsJsonAsync("/api/tasks", new { title = "nope" })).StatusCode);

        var ok = await _user.PostAsJsonAsync("/api/tasks", new { title = "signed" });
        Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
        var created = await ok.Content.ReadFromJsonAsync<TaskItem>(Json);
        Assert.Equal("demo@techtest.dev", created!.CreatedBy);
    }

    [Fact]
    public async Task Preferences_are_private_to_the_subject_unless_admin()
    {
        var mine = await _user.PutAsJsonAsync("/api/preferences/user-7", new { theme = "dark" });
        Assert.Equal(HttpStatusCode.OK, mine.StatusCode);

        var someoneElse = await _user.PutAsJsonAsync("/api/preferences/user-8", new { theme = "dark" });
        Assert.Equal(HttpStatusCode.Forbidden, someoneElse.StatusCode);

        var byAdmin = await _admin.PutAsJsonAsync("/api/preferences/user-8", new { theme = "light" });
        Assert.Equal(HttpStatusCode.OK, byAdmin.StatusCode);
    }

    [Fact]
    public async Task Uploads_belong_to_their_uploader()
    {
        using var form = new MultipartFormDataContent();
        var content = new ByteArrayContent("hello"u8.ToArray());
        content.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        form.Add(content, "files", "mine.txt");
        var created = await (await _user.PostAsync("/api/uploads", form)).Content.ReadFromJsonAsync<List<UploadItem>>(Json);
        var id = created![0].Id;
        Assert.Equal("demo@techtest.dev", created[0].OwnerId);

        using var other = factory.CreateClient("9", "viewer@techtest.dev", "Viewer");
        Assert.Equal(HttpStatusCode.Forbidden, (await other.DeleteAsync($"/api/uploads/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _anonymous.DeleteAsync($"/api/uploads/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _anonymous.GetAsync($"/api/uploads/{id}/content")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await _admin.DeleteAsync($"/api/uploads/{id}")).StatusCode);
    }

    [Fact]
    public async Task ETag_and_If_Match_refuse_lost_updates()
    {
        var created = await (await _user.PostAsJsonAsync("/api/tasks", new { title = "etag" })).Content.ReadFromJsonAsync<TaskItem>(Json);
        var read = await _user.GetAsync($"/api/tasks/{created!.Id}");
        var etag = read.Headers.ETag;
        Assert.NotNull(etag);
        Assert.True(etag.IsWeak);

        using var stale = new HttpRequestMessage(HttpMethod.Put, $"/api/tasks/{created.Id}") { Content = JsonContent.Create(new { title = "first", status = "InProgress" }) };
        stale.Headers.IfMatch.Add(etag);
        Assert.Equal(HttpStatusCode.OK, (await _user.SendAsync(stale)).StatusCode);

        using var second = new HttpRequestMessage(HttpMethod.Put, $"/api/tasks/{created.Id}") { Content = JsonContent.Create(new { title = "second", status = "Done" }) };
        second.Headers.IfMatch.Add(etag);
        var refused = await _user.SendAsync(second);
        Assert.Equal(HttpStatusCode.PreconditionFailed, refused.StatusCode);
        Assert.Equal("application/problem+json", refused.Content.Headers.ContentType?.MediaType);

        var current = await _user.GetFromJsonAsync<TaskItem>($"/api/tasks/{created.Id}", Json);
        Assert.Equal("first", current!.Title);

        var withoutIfMatch = await _user.PutAsJsonAsync($"/api/tasks/{created.Id}", new { title = "third", status = "Done" });
        Assert.Equal(HttpStatusCode.OK, withoutIfMatch.StatusCode);
    }

    [Fact]
    public async Task Soft_deleted_tasks_leave_stats_and_lists_and_only_admin_purges()
    {
        var created = await (await _user.PostAsJsonAsync("/api/tasks", new { title = "soft " + Guid.NewGuid() })).Content.ReadFromJsonAsync<TaskItem>(Json);
        Assert.Equal(HttpStatusCode.NoContent, (await _user.DeleteAsync($"/api/tasks/{created!.Id}")).StatusCode);

        var page = await _user.GetFromJsonAsync<PagedResponse<TaskItem>>("/api/tasks?pageSize=100&q=soft", Json);
        Assert.DoesNotContain(page!.Items, t => t.Id == created.Id);
        var withDeleted = await _user.GetFromJsonAsync<PagedResponse<TaskItem>>("/api/tasks?pageSize=100&q=soft&includeDeleted=true", Json);
        Assert.Contains(withDeleted!.Items, t => t.Id == created.Id && t.DeletedAt != null);

        Assert.Equal(HttpStatusCode.Forbidden, (await _user.DeleteAsync($"/api/tasks/{created.Id}?permanent=true")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await _admin.DeleteAsync($"/api/tasks/{created.Id}?permanent=true")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _user.GetAsync($"/api/tasks/{created.Id}?includeDeleted=true")).StatusCode);
    }

    [Fact]
    public async Task Audit_trail_records_who_did_what()
    {
        var title = "audited " + Guid.NewGuid().ToString("N")[..6];
        var created = await (await _user.PostAsJsonAsync("/api/tasks", new { title })).Content.ReadFromJsonAsync<TaskItem>(Json);
        await _user.PutAsJsonAsync($"/api/tasks/{created!.Id}", new { title, status = "Done" });

        // Reading the trail needs a session; it names people.
        Assert.Equal(HttpStatusCode.Unauthorized, (await _anonymous.GetAsync("/api/audit?resource=task")).StatusCode);
        var entries = await _user.GetFromJsonAsync<List<AuditEntry>>("/api/audit?resource=task&limit=50", Json);
        var mine = entries!.Where(e => e.TargetId == created.Id.ToString()).ToList();
        Assert.Contains(mine, e => e.Action == "create" && e.Actor == "demo@techtest.dev" && e.Summary == title);
        Assert.Contains(mine, e => e.Action == "move" && e.Summary.EndsWith("Done"));
        Assert.True(mine[0].At >= mine[^1].At);

        // one task's history carries the field diff of each update
        var history = await _user.GetFromJsonAsync<List<AuditEntry>>($"/api/tasks/{created.Id}/history", Json);
        Assert.Equal(["move", "create"], history!.Select(h => h.Action));
        Assert.Equal(["status"], history![0].Changes!.Keys);
        Assert.Equal("Todo", ((JsonElement)history[0].Changes!["status"].From!).GetString());
        Assert.Equal("Done", ((JsonElement)history[0].Changes!["status"].To!).GetString());
        Assert.Equal(HttpStatusCode.Unauthorized, (await _anonymous.GetAsync($"/api/tasks/{created.Id}/history")).StatusCode);

        Assert.Equal(HttpStatusCode.BadRequest, (await _user.GetAsync("/api/audit?limit=0")).StatusCode);

        // Sign-in events reported by part A: ingest needs the internal token, reading them needs Admin.
        var evt = new { actor = "viewer@techtest.dev", action = "signin-failed", resource = "auth", targetId = "6", summary = "wrong password from 203.0.113.9 (2 attempts left)" };
        Assert.Equal(HttpStatusCode.Forbidden, (await _anonymous.PostAsJsonAsync("/api/audit", evt)).StatusCode);
        using var ingest = new HttpRequestMessage(HttpMethod.Post, "/api/audit") { Content = JsonContent.Create(evt) };
        ingest.Headers.Add("X-Internal-Token", ApiFactory.AuditToken);
        Assert.Equal(HttpStatusCode.Accepted, (await _anonymous.SendAsync(ingest)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _user.GetAsync("/api/audit?resource=auth")).StatusCode);
        var auth = await _admin.GetFromJsonAsync<List<AuditEntry>>("/api/audit?resource=auth&limit=5", Json);
        Assert.Contains(auth!, e => e.Action == "signin-failed" && e.Actor == "viewer@techtest.dev");
        var everything = await _user.GetFromJsonAsync<List<AuditEntry>>("/api/audit?limit=200", Json);
        Assert.DoesNotContain(everything!, e => e.Resource == "auth"); // non-admins never see sign-in events, even unfiltered
    }

    [Fact]
    public async Task Catalog_schema_is_enforced_per_kind_and_paging_reports_totals()
    {
        var kind = "k" + Guid.NewGuid().ToString("N")[..10];
        await _user.PostAsJsonAsync($"/api/catalog/{kind}", new { code = "jakarta", label = "Jakarta", attributes = new { lat = -6.2, lng = 106.8 } });

        Assert.Equal(HttpStatusCode.Forbidden, (await _user.PutAsJsonAsync($"/api/catalog/{kind}/_schema", new { schema = new { type = "object" } })).StatusCode);

        var schema = new { type = "object", required = new[] { "lat", "lng" }, properties = new { lat = new { type = "number", minimum = -90, maximum = 90 }, lng = new { type = "number" } } };
        var put = await _admin.PutAsJsonAsync($"/api/catalog/{kind}/_schema", new { schema });
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _anonymous.GetAsync($"/api/catalog/{kind}/_schema")).StatusCode);

        var missing = await _user.PostAsJsonAsync($"/api/catalog/{kind}", new { code = "nowhere", label = "Nowhere" });
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        var body = await missing.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("lat", body.GetProperty("errors").GetProperty("attributes")[0].GetString());

        var outOfRange = await _user.PostAsJsonAsync($"/api/catalog/{kind}", new { code = "mars", label = "Mars", attributes = new { lat = 400, lng = 1 } });
        Assert.Equal(HttpStatusCode.BadRequest, outOfRange.StatusCode);

        Assert.Equal(HttpStatusCode.Created, (await _user.PostAsJsonAsync($"/api/catalog/{kind}", new { code = "bandung", label = "Bandung", attributes = new { lat = -6.9, lng = 107.6 } })).StatusCode);

        var tightened = await _admin.PutAsJsonAsync($"/api/catalog/{kind}/_schema", new { schema = new { type = "object", required = new[] { "population" } } });
        Assert.Equal(HttpStatusCode.BadRequest, tightened.StatusCode);

        var firstPage = await _anonymous.GetAsync($"/api/catalog/{kind}?pageSize=1&page=2");
        Assert.Equal("2", firstPage.Headers.GetValues("X-Total-Count").Single());
        Assert.Single((await firstPage.Content.ReadFromJsonAsync<List<CatalogItem>>(Json))!);

        Assert.Equal(HttpStatusCode.NoContent, (await _admin.DeleteAsync($"/api/catalog/{kind}/_schema")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await _user.PostAsJsonAsync($"/api/catalog/{kind}", new { code = "free", label = "Free again" })).StatusCode);
    }

    [Fact]
    public async Task Writes_are_rate_limited_per_address()
    {
        using var limited = ApiFactory.ForDatabase(factory.DatabaseName)
            .WithWebHostBuilder(b => b.UseSetting("Api:WritesPerMinute", "3"));
        using var client = limited.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Issue("1", "test@techtest.dev", ["TestGroup"]));

        HttpResponseMessage? last = null;
        for (var i = 0; i < 4; i++)
        {
            last = await client.PostAsJsonAsync("/api/tasks", new { title = "burst " + i });
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, last!.StatusCode);
        Assert.Equal("application/problem+json", last.Content.Headers.ContentType?.MediaType);
        Assert.NotNull(last.Headers.RetryAfter);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/tasks")).StatusCode);
    }

    [Fact]
    public async Task Metrics_endpoint_serves_prometheus_text()
    {
        await _anonymous.GetAsync("/api/tasks");
        var response = await _anonymous.GetAsync("/metrics");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        Assert.Contains("http_request_duration_seconds", text);
    }
}
