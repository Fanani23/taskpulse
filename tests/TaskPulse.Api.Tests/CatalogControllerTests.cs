using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TaskPulse.Api.Models;
using Xunit;

namespace TaskPulse.Api.Tests;

public sealed class CatalogControllerTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _client = factory.CreateClient("1", "test@techtest.dev", "TestGroup");

    [Fact]
    public async Task Crud_round_trip_with_derived_code_and_attributes()
    {
        var kind = Kind();

        var create = await _client.PostAsJsonAsync($"/api/catalog/{kind}", new { label = "  South East Asia! ", attributes = new { lat = -6.2, note = "hub" }, sort = 2 });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var created = await create.Content.ReadFromJsonAsync<CatalogItem>(Json);
        Assert.NotNull(created);
        Assert.Equal("south-east-asia", created.Code);
        Assert.Equal("South East Asia!", created.Label);
        Assert.Equal(2, created.Sort);
        Assert.Equal(-6.2, created.Attributes!.Value.GetProperty("lat").GetDouble());
        Assert.Equal($"/api/catalog/{kind}/south-east-asia", create.Headers.Location?.ToString());

        var fetched = await _client.GetFromJsonAsync<CatalogItem>($"/api/catalog/{kind}/south-east-asia", Json);
        Assert.Equal(created.Id, fetched!.Id);
        Assert.Equal("hub", fetched.Attributes!.Value.GetProperty("note").GetString());

        var update = await _client.PutAsJsonAsync($"/api/catalog/{kind}/south-east-asia", new { label = "SEA", attributes = new { note = "renamed" }, sort = 1 });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var updated = await update.Content.ReadFromJsonAsync<CatalogItem>(Json);
        Assert.Equal("SEA", updated!.Label);
        Assert.Equal(1, updated.Sort);
        Assert.False(updated.Attributes!.Value.TryGetProperty("lat", out _));

        var kinds = await _client.GetFromJsonAsync<List<CatalogKindSummary>>("/api/catalog", Json);
        Assert.Contains(kinds!, k => k.Kind == kind && k.Count == 1);

        var delete = await _client.DeleteAsync($"/api/catalog/{kind}/south-east-asia");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/catalog/{kind}/south-east-asia")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.DeleteAsync($"/api/catalog/{kind}/south-east-asia")).StatusCode);
    }

    [Fact]
    public async Task Duplicate_code_in_the_same_kind_is_a_conflict()
    {
        var kind = Kind();
        Assert.Equal(HttpStatusCode.Created, (await _client.PostAsJsonAsync($"/api/catalog/{kind}", new { code = "asia", label = "Asia" })).StatusCode);

        var duplicate = await _client.PostAsJsonAsync($"/api/catalog/{kind}", new { label = "Asia" });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal("application/problem+json", duplicate.Content.Headers.ContentType?.MediaType);

        var otherKind = await _client.PostAsJsonAsync($"/api/catalog/{Kind()}", new { code = "asia", label = "Asia" });
        Assert.Equal(HttpStatusCode.Created, otherKind.StatusCode);
    }

    [Fact]
    public async Task Parent_filter_search_and_ordering()
    {
        var regions = Kind();
        var countries = Kind();
        await _client.PostAsJsonAsync($"/api/catalog/{regions}", new { code = "asia", label = "Asia" });
        await _client.PostAsJsonAsync($"/api/catalog/{regions}", new { code = "europe", label = "Europe" });
        await _client.PostAsJsonAsync($"/api/catalog/{countries}", new { label = "Russia", parents = new[] { "asia", "europe" }, sort = 5 });
        await _client.PostAsJsonAsync($"/api/catalog/{countries}", new { label = "Japan", parents = new[] { "asia" } });
        await _client.PostAsJsonAsync($"/api/catalog/{countries}", new { label = "France", parents = new[] { "europe" } });

        var asia = await _client.GetFromJsonAsync<List<CatalogItem>>($"/api/catalog/{countries}?parent=asia", Json);
        Assert.Equal(["japan", "russia"], asia!.Select(c => c.Code));

        var all = await _client.GetFromJsonAsync<List<CatalogItem>>($"/api/catalog/{countries}", Json);
        Assert.Equal(["france", "japan", "russia"], all!.Select(c => c.Code));

        var search = await _client.GetFromJsonAsync<List<CatalogItem>>($"/api/catalog/{countries}?q=RUS", Json);
        Assert.Single(search!);
        Assert.Equal("russia", search![0].Code);

        Assert.Empty((await _client.GetFromJsonAsync<List<CatalogItem>>($"/api/catalog/{Kind()}", Json))!);
    }

    [Fact]
    public async Task Deleting_a_parent_removes_it_from_children()
    {
        var regions = Kind();
        var countries = Kind();
        await _client.PostAsJsonAsync($"/api/catalog/{regions}", new { code = "me", label = "Middle East" });
        await _client.PostAsJsonAsync($"/api/catalog/{regions}", new { code = "africa", label = "Africa" });
        await _client.PostAsJsonAsync($"/api/catalog/{countries}", new { code = "egypt", label = "Egypt", parents = new[] { "me", "africa" } });

        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/catalog/{regions}/me")).StatusCode);

        var egypt = await _client.GetFromJsonAsync<CatalogItem>($"/api/catalog/{countries}/egypt", Json);
        Assert.Equal(["africa"], egypt!.Parents);
        Assert.Empty((await _client.GetFromJsonAsync<List<CatalogItem>>($"/api/catalog/{countries}?parent=me", Json))!);
    }

    [Fact]
    public async Task Invalid_input_is_rejected()
    {
        var kind = Kind();

        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/catalog/Not_A_Kind")).StatusCode);

        var noLabel = await _client.PostAsJsonAsync($"/api/catalog/{kind}", new { label = "  " });
        Assert.Equal(HttpStatusCode.BadRequest, noLabel.StatusCode);

        var badCode = await _client.PostAsJsonAsync($"/api/catalog/{kind}", new { code = "Bad Code", label = "x" });
        Assert.Equal(HttpStatusCode.BadRequest, badCode.StatusCode);

        var badParent = await _client.PostAsJsonAsync($"/api/catalog/{kind}", new { label = "x", parents = new[] { "not valid" } });
        Assert.Equal(HttpStatusCode.BadRequest, badParent.StatusCode);

        var badAttributes = await _client.PostAsJsonAsync($"/api/catalog/{kind}", new { label = "x", attributes = new[] { 1, 2 } });
        Assert.Equal(HttpStatusCode.BadRequest, badAttributes.StatusCode);

        var unsluggable = await _client.PostAsJsonAsync($"/api/catalog/{kind}", new { label = "!!!" });
        Assert.Equal(HttpStatusCode.BadRequest, unsluggable.StatusCode);
        var body = await unsluggable.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("errors").TryGetProperty("code", out _));

        Assert.Equal(HttpStatusCode.NotFound, (await _client.PutAsJsonAsync($"/api/catalog/{kind}/missing", new { label = "x" })).StatusCode);
    }

    private static string Kind() => "k" + Guid.NewGuid().ToString("N")[..10];
}
