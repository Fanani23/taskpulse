using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text;
using System.Text.Json;
using TaskPulse.Api.Infrastructure;
using TaskPulse.Api.Models;

namespace TaskPulse.Api.Services;

public sealed record ImportRowError(int Row, string Error);

public sealed record ImportResult(int Created, int Updated, IReadOnlyList<ImportRowError> Skipped);

// CSV in and out for tasks and catalog kinds. Export writes what the list endpoints return, with the same filters;
// import goes through the same services as the API (validation, audit, change events), one row at a time, and never
// stops at a bad row - the row is reported and the rest continues. Rows with an id/code that exists are updated.
public sealed class CsvTransferService(ITaskService tasks, ICatalogService catalog, IAuditService audit)
{
    public const int MaxRows = 2000;
    public static readonly string[] TaskColumns = ["id", "title", "description", "status", "priority", "dueAt", "assigneeId", "assigneeName", "labels", "createdAt", "updatedAt"];
    public static readonly string[] CatalogColumns = ["code", "label", "parents", "attributes", "sort", "updatedAt"];

    public async Task<string> ExportTasksAsync(TaskListQuery query, CancellationToken cancellationToken)
    {
        var sb = new StringBuilder().AppendLine(Csv.Row(TaskColumns));
        var page = 1;
        while (true)
        {
            var batch = await tasks.ListAsync(query with { Page = page, PageSize = 100 }, cancellationToken);
            foreach (var t in batch.Items)
            {
                sb.AppendLine(Csv.Row(t.Id.ToString(), t.Title, t.Description, t.Status.ToString(), t.Priority.ToString(), t.DueAt?.ToString("O"), t.AssigneeId, t.AssigneeName, string.Join("|", t.Labels), t.CreatedAt.ToString("O"), t.UpdatedAt.ToString("O")));
            }

            if (batch.Items.Count < 100 || page * 100 >= batch.Total)
            {
                break;
            }

            page++;
        }

        return sb.ToString();
    }

    public async Task<string> ExportCatalogAsync(string kind, CancellationToken cancellationToken)
    {
        var sb = new StringBuilder().AppendLine(Csv.Row(CatalogColumns));
        var (items, _, _, _) = await catalog.ListAsync(kind, new CatalogListQuery { PageSize = CatalogLimits.ListMax }, cancellationToken);
        foreach (var c in items)
        {
            sb.AppendLine(Csv.Row(c.Code, c.Label, string.Join("|", c.Parents), c.Attributes?.GetRawText(), c.Sort.ToString(CultureInfo.InvariantCulture), c.UpdatedAt.ToString("O")));
        }

        return sb.ToString();
    }

    public async Task<ImportResult> ImportTasksAsync(string csv, CancellationToken cancellationToken)
    {
        var (header, rows) = Read(csv);
        int Col(string name) => Array.IndexOf(header, name);
        if (Col("title") < 0)
        {
            throw new ValidationException("The CSV needs a 'title' column.");
        }

        var created = 0;
        var updated = 0;
        var skipped = new List<ImportRowError>();
        for (var r = 0; r < rows.Count; r++)
        {
            var row = rows[r];
            string? Get(string name) { var i = Col(name); return i >= 0 && i < row.Count && row[i].Length > 0 ? row[i] : null; }
            try
            {
                var priority = ParseEnum<TaskPriority>(Get("priority"), "priority");
                var status = ParseEnum<TaskItemStatus>(Get("status"), "status");
                var due = Get("dueAt") is { } d
                    ? DateTimeOffset.TryParse(d, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed) ? parsed : throw new ValidationException($"dueAt '{d}' is not a date (use ISO 8601, e.g. 2030-01-02 or 2030-01-02T10:00:00Z)")
                    : (DateTimeOffset?)null;
                var labels = Get("labels")?.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var id = Get("id") is { } raw && Guid.TryParse(raw, out var g) ? g : (Guid?)null;
                var existing = id is { } gid ? await tasks.GetAsync(gid, false, cancellationToken) : null;
                if (existing is not null)
                {
                    var result = await tasks.UpdateAsync(existing.Id, Validated(new UpdateTaskRequest
                    {
                        Title = Get("title"),
                        Description = Get("description"),
                        Status = status ?? existing.Status,
                        Priority = priority,
                        DueAt = due,
                        AssigneeId = Get("assigneeId"),
                        AssigneeName = Get("assigneeName"),
                        Labels = labels,
                    }), null, cancellationToken);
                    if (result.Outcome != WriteOutcome.Ok)
                    {
                        throw new ValidationException(result.Outcome.ToString());
                    }

                    updated++;
                }
                else
                {
                    var item = await tasks.CreateAsync(Validated(new CreateTaskRequest
                    {
                        Title = Get("title"),
                        Description = Get("description"),
                        Priority = priority,
                        DueAt = due,
                        AssigneeId = Get("assigneeId"),
                        AssigneeName = Get("assigneeName"),
                        Labels = labels,
                    }), cancellationToken);
                    if (status is { } wanted && wanted != TaskItemStatus.Todo)
                    {
                        await tasks.UpdateAsync(item.Id, new UpdateTaskRequest { Title = item.Title, Description = item.Description, Status = wanted, Priority = item.Priority, DueAt = item.DueAt, AssigneeId = item.AssigneeId, AssigneeName = item.AssigneeName, Labels = [.. item.Labels] }, null, cancellationToken);
                    }

                    created++;
                }
            }
            catch (Exception e) when (e is ValidationException or FormatException or ArgumentException)
            {
                skipped.Add(new ImportRowError(r + 2, e.Message)); // +2: header line and 1-based
            }
        }

        await audit.RecordAsync("import", "task", "csv", $"imported {created} new, {updated} updated, {skipped.Count} skipped", cancellationToken: cancellationToken);
        return new ImportResult(created, updated, skipped);
    }

    public async Task<ImportResult> ImportCatalogAsync(string kind, string csv, CancellationToken cancellationToken)
    {
        var (header, rows) = Read(csv);
        int Col(string name) => Array.IndexOf(header, name);
        if (Col("label") < 0)
        {
            throw new ValidationException("The CSV needs a 'label' column (and usually 'code').");
        }

        var created = 0;
        var updated = 0;
        var skipped = new List<ImportRowError>();
        for (var r = 0; r < rows.Count; r++)
        {
            var row = rows[r];
            string? Get(string name) { var i = Col(name); return i >= 0 && i < row.Count && row[i].Length > 0 ? row[i] : null; }
            try
            {
                var code = Get("code");
                var parents = Get("parents")?.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                JsonElement? attributes = Get("attributes") is { } a ? JsonDocument.Parse(a).RootElement.Clone() : null;
                var sort = Get("sort") is { } s ? int.Parse(s, CultureInfo.InvariantCulture) : (int?)null;
                var existing = code is not null ? await catalog.GetAsync(kind, code, cancellationToken) : null;
                var result = existing is not null
                    ? await catalog.UpdateAsync(kind, code!, Validated(new UpdateCatalogItemRequest { Label = Get("label"), Parents = parents, Attributes = attributes, Sort = sort }), null, cancellationToken)
                    : await catalog.CreateAsync(kind, Validated(new CreateCatalogItemRequest { Code = code, Label = Get("label"), Parents = parents, Attributes = attributes, Sort = sort }), cancellationToken);
                if (result.Outcome != CatalogWriteOutcome.Ok)
                {
                    throw new ValidationException(result.Errors is { Count: > 0 } errs ? string.Join("; ", errs) : result.Outcome.ToString());
                }

                if (existing is not null)
                {
                    updated++;
                }
                else
                {
                    created++;
                }
            }
            catch (Exception e) when (e is ValidationException or FormatException or ArgumentException or JsonException)
            {
                skipped.Add(new ImportRowError(r + 2, e.Message));
            }
        }

        await audit.RecordAsync("import", "catalog", kind, $"imported {created} new, {updated} updated, {skipped.Count} skipped", kind, cancellationToken);
        return new ImportResult(created, updated, skipped);
    }

    private static T? ParseEnum<T>(string? value, string column) where T : struct, Enum
    {
        if (value is null)
        {
            return null;
        }

        return Enum.TryParse<T>(value, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : throw new ValidationException($"{column} '{value}' is not one of {string.Join(", ", Enum.GetNames<T>())}");
    }

    // The services expect what [ApiController] validates for them; an imported row goes through the same annotations.
    private static T Validated<T>(T request) where T : notnull
    {
        var results = new List<ValidationResult>();
        if (!Validator.TryValidateObject(request, new ValidationContext(request), results, validateAllProperties: true))
        {
            throw new ValidationException(string.Join("; ", results.Select(r => r.ErrorMessage)));
        }

        return request;
    }

    private static (string[] Header, List<List<string>> Rows) Read(string csv)
    {
        var all = Csv.Parse(csv);
        if (all.Count == 0)
        {
            throw new ValidationException("The CSV is empty.");
        }

        if (all.Count - 1 > MaxRows)
        {
            throw new ValidationException($"At most {MaxRows} rows per import.");
        }

        var header = all[0].Select(h => h.Trim()).ToArray();
        return (header, all.Skip(1).ToList());
    }
}
