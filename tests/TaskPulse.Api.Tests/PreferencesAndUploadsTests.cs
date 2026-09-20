using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using TaskPulse.Api.Models;
using Xunit;

namespace TaskPulse.Api.Tests;

public sealed class PreferencesAndUploadsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static readonly byte[] PngBytes =
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
        0x89, 0x00, 0x00, 0x00, 0x0A, 0x49, 0x44, 0x41, 0x54, 0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00,
        0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE,
        0x42, 0x60, 0x82,
    ];

    private readonly HttpClient _client = factory.CreateClient("1", "test@techtest.dev", "Admin");

    [Fact]
    public async Task Preferences_are_absent_until_saved_then_upserted()
    {
        var userId = "user-" + Guid.NewGuid().ToString("N")[..6];
        var defaults = await _client.GetFromJsonAsync<Preferences>($"/api/preferences/{userId}", Json);
        Assert.False(defaults!.Saved);
        Assert.Equal("system", defaults.Theme);
        Assert.Null(defaults.UpdatedAt);

        var save = await _client.PutAsJsonAsync($"/api/preferences/{userId}", new { theme = "dark", nickname = "  Pram  " });
        Assert.Equal(HttpStatusCode.OK, save.StatusCode);
        var saved = await save.Content.ReadFromJsonAsync<Preferences>(Json);
        Assert.Equal("dark", saved!.Theme);
        Assert.Equal("Pram", saved.Nickname);
        Assert.True(saved.Saved);

        var again = await _client.PutAsJsonAsync($"/api/preferences/{userId}", new { theme = "light", nickname = "" });
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        var fetched = await _client.GetFromJsonAsync<Preferences>($"/api/preferences/{userId}", Json);
        Assert.Equal("light", fetched!.Theme);
        Assert.Null(fetched.Nickname);
        Assert.True(fetched.UpdatedAt >= saved.UpdatedAt);

        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PutAsJsonAsync($"/api/preferences/{userId}", new { theme = "neon" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.PutAsJsonAsync("/api/preferences/bad id", new { theme = "dark" })).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/preferences/{userId}")).StatusCode);
        Assert.False((await _client.GetFromJsonAsync<Preferences>($"/api/preferences/{userId}", Json))!.Saved);
    }

    [Fact]
    public async Task Upload_round_trip_lists_downloads_and_deletes()
    {
        using var form = new MultipartFormDataContent();
        form.Add(Png("signature.png"), "files", "signature.png");
        form.Add(new StringContent("signpad"), "source");
        form.Add(new StringContent("drawn in the test"), "note");

        var create = await _client.PostAsync("/api/uploads", form);
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var items = await create.Content.ReadFromJsonAsync<List<UploadItem>>(Json);
        var item = Assert.Single(items!);
        Assert.Equal("signature.png", item.FileName);
        Assert.Equal("image/png", item.ContentType);
        Assert.Equal(PngBytes.Length, item.Size);
        Assert.Equal("signpad", item.Source);
        Assert.Equal("drawn in the test", item.Note);

        var listed = await _client.GetFromJsonAsync<List<UploadItem>>("/api/uploads?source=signpad", Json);
        Assert.Contains(listed!, u => u.Id == item.Id);
        Assert.DoesNotContain((await _client.GetFromJsonAsync<List<UploadItem>>("/api/uploads?source=other", Json))!, u => u.Id == item.Id);

        var download = await _client.GetAsync($"/api/uploads/{item.Id}/content");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal("image/png", download.Content.Headers.ContentType?.MediaType);
        Assert.Equal(PngBytes, await download.Content.ReadAsByteArrayAsync());

        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/uploads/{item.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/uploads/{item.Id}/content")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.DeleteAsync($"/api/uploads/{item.Id}")).StatusCode);
    }

    [Fact]
    public async Task Uploads_are_rejected_when_too_large_mislabelled_or_missing()
    {
        using var big = new MultipartFormDataContent();
        var bigBytes = new ByteArrayContent([.. PngBytes, .. new byte[3 * 1024 * 1024]]);
        bigBytes.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        big.Add(bigBytes, "files", "big.png");
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await _client.PostAsync("/api/uploads", big)).StatusCode);

        using var fake = new MultipartFormDataContent();
        var text = new ByteArrayContent(Encoding.UTF8.GetBytes("<script>alert(1)</script>"));
        text.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        fake.Add(text, "files", "not-really.png");
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, (await _client.PostAsync("/api/uploads", fake)).StatusCode);

        using var html = new MultipartFormDataContent();
        var page = new ByteArrayContent(Encoding.UTF8.GetBytes("<html></html>"));
        page.Headers.ContentType = new MediaTypeHeaderValue("text/html");
        html.Add(page, "files", "page.html");
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, (await _client.PostAsync("/api/uploads", html)).StatusCode);

        using var empty = new MultipartFormDataContent();
        empty.Add(new StringContent("form"), "source");
        var none = await _client.PostAsync("/api/uploads", empty);
        Assert.Equal(HttpStatusCode.BadRequest, none.StatusCode);
    }

    private static ByteArrayContent Png(string name)
    {
        var content = new ByteArrayContent(PngBytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        return content;
    }
}
