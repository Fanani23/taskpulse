using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using TaskPulse.Api.Infrastructure;
using TaskPulse.Api.Models;
using TaskPulse.Api.Services;
using Xunit;

namespace TaskPulse.Api.Tests;

public sealed class CsvTransferTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };
    private readonly HttpClient _client = factory.CreateClient("1", "csv@techtest.dev", "Admin");

    [Fact]
    public void Csv_parser_handles_quotes_commas_newlines_and_bom()
    {
        var rows = Csv.Parse("﻿a,b,c\r\n1,\"x, y\",\"say \"\"hi\"\"\"\n\n2,\"multi\nline\",\n");
        Assert.Equal(["a", "b", "c"], rows[0]);
        Assert.Equal(["1", "x, y", "say \"hi\""], rows[1]);
        Assert.Equal(["2", "multi\nline", ""], rows[2]);
        Assert.Equal(3, rows.Count);
        Assert.Equal("\"x, y\",plain,\"q\"\"q\"", Csv.Row("x, y", "plain", "q\"q"));
    }

    [Fact]
    public async Task Tasks_export_then_import_round_trip_and_bad_rows_are_reported()
    {
        var marker = Guid.NewGuid().ToString("N")[..6];
        var created = await (await _client.PostAsJsonAsync("/api/tasks", new { title = $"csv {marker} one", priority = "High", labels = new[] { "csv", marker } })).Content.ReadFromJsonAsync<TaskItem>(Json);

        var export = await _client.GetAsync($"/api/tasks/export.csv?q={marker}");
        Assert.Equal(HttpStatusCode.OK, export.StatusCode);
        Assert.StartsWith("text/csv", export.Content.Headers.ContentType!.ToString());
        var text = await export.Content.ReadAsStringAsync();
        var rows = Csv.Parse(text);
        Assert.Equal(CsvTransferService.TaskColumns, rows[0]);
        var exported = rows.Skip(1).Single(r => r[0] == created!.Id.ToString());
        Assert.Equal("High", exported[4]);
        Assert.Equal($"csv|{marker}", exported[8]);

        // Import: one update (by id), one new, one bad priority, one without a title.
        var csv = Csv.Row(CsvTransferService.TaskColumns) + "\n"
                + Csv.Row(created!.Id.ToString(), $"csv {marker} one (renamed)", "", "InProgress", "Low", "", "", "", "csv") + "\n"
                + Csv.Row("", $"csv {marker} two", "from the file", "Done", "Normal", "2030-01-02T10:00:00Z", "1", "csv@techtest.dev", $"csv|{marker}|new") + "\n"
                + Csv.Row("", $"csv {marker} bad", "", "", "Urgent") + "\n"
                + Csv.Row("", "", "no title") + "\n";
        var import = await _client.PostAsync("/api/tasks/import", new StringContent(csv, Encoding.UTF8, "text/csv"));
        Assert.Equal(HttpStatusCode.OK, import.StatusCode);
        var result = await import.Content.ReadFromJsonAsync<ImportResult>(Json);
        Assert.Equal(1, result!.Created);
        Assert.Equal(1, result.Updated);
        Assert.Equal(2, result.Skipped.Count);
        Assert.Contains(result.Skipped, s => s.Row == 4);

        var renamed = await _client.GetFromJsonAsync<TaskItem>($"/api/tasks/{created.Id}", Json);
        Assert.Equal($"csv {marker} one (renamed)", renamed!.Title);
        Assert.Equal(TaskItemStatus.InProgress, renamed.Status);
        Assert.Equal(TaskPriority.Low, renamed.Priority);
        var two = await _client.GetFromJsonAsync<PagedResponse<TaskItem>>($"/api/tasks?q={marker}%20two", Json);
        Assert.Single(two!.Items);
        Assert.Equal(TaskItemStatus.Done, two.Items[0].Status);
        Assert.Equal("csv@techtest.dev", two.Items[0].AssigneeName);
        Assert.Contains("new", two.Items[0].Labels);

        // Anonymous import is refused; an empty body is a 400 with a csv error.
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().PostAsync("/api/tasks/import", new StringContent(csv, Encoding.UTF8, "text/csv"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsync("/api/tasks/import", new StringContent("", Encoding.UTF8, "text/csv"))).StatusCode);
    }

    [Fact]
    public async Task Catalog_import_creates_and_updates_by_code_and_export_mirrors_it()
    {
        var kind = "csvkind" + Guid.NewGuid().ToString("N")[..5];
        var csv = Csv.Row(CsvTransferService.CatalogColumns) + "\n"
                + Csv.Row("alpha", "Alpha", "", "{\"lat\":1}", "1") + "\n"
                + Csv.Row("beta", "Beta", "alpha", "", "2") + "\n"
                + Csv.Row("bad code!", "Bad", "", "", "3") + "\n";
        var first = await (await _client.PostAsync($"/api/catalog/{kind}/import", new StringContent(csv, Encoding.UTF8, "text/csv"))).Content.ReadFromJsonAsync<ImportResult>(Json);
        Assert.Equal(2, first!.Created);
        Assert.Single(first.Skipped);

        var again = await (await _client.PostAsync($"/api/catalog/{kind}/import", new StringContent(Csv.Row(CsvTransferService.CatalogColumns) + "\n" + Csv.Row("alpha", "Alpha renamed", "", "", "9") + "\n", Encoding.UTF8, "text/csv"))).Content.ReadFromJsonAsync<ImportResult>(Json);
        Assert.Equal(0, again!.Created);
        Assert.Equal(1, again.Updated);

        var export = Csv.Parse(await _client.GetStringAsync($"/api/catalog/{kind}/export.csv"));
        Assert.Equal(CsvTransferService.CatalogColumns, export[0]);
        var alpha = export.Skip(1).Single(r => r[0] == "alpha");
        Assert.Equal("Alpha renamed", alpha[1]);
        Assert.Equal("9", alpha[4]);
        var beta = export.Skip(1).Single(r => r[0] == "beta");
        Assert.Equal("alpha", beta[2]);
    }
}
