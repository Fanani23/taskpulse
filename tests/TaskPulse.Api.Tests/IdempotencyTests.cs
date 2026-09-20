using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TaskPulse.Api.Infrastructure;
using TaskPulse.Api.Models;
using Xunit;

namespace TaskPulse.Api.Tests;

public sealed class IdempotencyTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };
    private readonly HttpClient _user = factory.CreateClient("7", "demo@techtest.dev", "Viewer");
    private readonly HttpClient _other = factory.CreateClient("8", "other@techtest.dev", "Viewer");

    private static readonly byte[] PngBytes =
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
        0x89, 0x00, 0x00, 0x00, 0x0A, 0x49, 0x44, 0x41, 0x54, 0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00,
        0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE,
        0x42, 0x60, 0x82,
    ];

    private static HttpRequestMessage Post(string path, object body, string key)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Add(IdempotencyLimits.Header, key);
        return request;
    }

    [Fact]
    public async Task A_retried_create_returns_the_first_task_instead_of_a_second_one()
    {
        var key = Guid.NewGuid().ToString();
        var title = "idempotent " + key[..8];
        var first = await _user.SendAsync(Post("/api/tasks", new { title }, key));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.False(first.Headers.Contains(IdempotencyLimits.ReplayedHeader));
        var created = (await first.Content.ReadFromJsonAsync<TaskItem>(Json))!;

        var again = await _user.SendAsync(Post("/api/tasks", new { title }, key));
        Assert.Equal(HttpStatusCode.Created, again.StatusCode);
        Assert.Equal("true", again.Headers.GetValues(IdempotencyLimits.ReplayedHeader).Single());
        Assert.Equal(first.Headers.Location, again.Headers.Location);
        Assert.Equal(created.Id, (await again.Content.ReadFromJsonAsync<TaskItem>(Json))!.Id);

        var list = await _user.GetFromJsonAsync<PagedResponse<TaskItem>>($"/api/tasks?q={Uri.EscapeDataString(title)}", Json);
        Assert.Equal(1, list!.Total);

        // the same key with another body is a mistake, not a replay
        var different = await _user.SendAsync(Post("/api/tasks", new { title = title + " changed" }, key));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, different.StatusCode);
        Assert.Equal("application/problem+json", different.Content.Headers.ContentType?.MediaType);

        // keys are per caller: someone else's identical request is their own create
        var other = await _other.SendAsync(Post("/api/tasks", new { title }, key));
        Assert.Equal(HttpStatusCode.Created, other.StatusCode);
        Assert.NotEqual(created.Id, (await other.Content.ReadFromJsonAsync<TaskItem>(Json))!.Id);

        // ...and per route: the same key on the catalog is a fresh request
        var kind = "idem-" + key[..6];
        Assert.Equal(HttpStatusCode.Created, (await _user.SendAsync(Post($"/api/catalog/{kind}", new { label = "Once" }, key))).StatusCode);
        var replayed = await _user.SendAsync(Post($"/api/catalog/{kind}", new { label = "Once" }, key));
        Assert.Equal(HttpStatusCode.Created, replayed.StatusCode);
        Assert.Equal("true", replayed.Headers.GetValues(IdempotencyLimits.ReplayedHeader).Single());
        Assert.Single((await _user.GetFromJsonAsync<List<CatalogItem>>($"/api/catalog/{kind}", Json))!);
    }

    [Fact]
    public async Task A_failed_create_is_not_remembered_and_a_long_key_is_refused()
    {
        var key = Guid.NewGuid().ToString();
        Assert.Equal(HttpStatusCode.BadRequest, (await _user.SendAsync(Post("/api/tasks", new { title = "" }, key))).StatusCode);
        // a request refused by validation never reached the action, so the key is still free for the corrected body
        Assert.Equal(HttpStatusCode.Created, (await _user.SendAsync(Post("/api/tasks", new { title = "fixed" }, key))).StatusCode);

        var tooLong = await _user.SendAsync(Post("/api/tasks", new { title = "x" }, new string('k', IdempotencyLimits.KeyMaxLength + 1)));
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);

        // no header: two posts, two tasks (nothing changes for clients that do not opt in)
        var title = "plain " + key[..8];
        await _user.PostAsJsonAsync("/api/tasks", new { title });
        await _user.PostAsJsonAsync("/api/tasks", new { title });
        Assert.Equal(2, (await _user.GetFromJsonAsync<PagedResponse<TaskItem>>($"/api/tasks?q={Uri.EscapeDataString(title)}", Json))!.Total);
    }

    [Fact]
    public async Task An_upload_retried_with_the_same_key_stores_the_file_once()
    {
        var key = Guid.NewGuid().ToString();
        HttpRequestMessage Upload()
        {
            var form = new MultipartFormDataContent();
            var bytes = new ByteArrayContent(PngBytes);
            bytes.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
            form.Add(bytes, "files", "idem.png");
            form.Add(new StringContent("idem"), "source");
            var request = new HttpRequestMessage(HttpMethod.Post, "/api/uploads") { Content = form };
            request.Headers.Add(IdempotencyLimits.Header, key);
            return request;
        }

        var first = await _user.SendAsync(Upload());
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var stored = (await first.Content.ReadFromJsonAsync<List<UploadItem>>(Json))!;
        var again = await _user.SendAsync(Upload());
        Assert.Equal(HttpStatusCode.Created, again.StatusCode);
        Assert.Equal(stored.Single().Id, (await again.Content.ReadFromJsonAsync<List<UploadItem>>(Json))!.Single().Id);
        Assert.Single((await _user.GetFromJsonAsync<List<UploadItem>>("/api/uploads?source=idem", Json))!);
    }
}
