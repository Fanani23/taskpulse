using System.ComponentModel.DataAnnotations;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TaskPulse.Api.Infrastructure;
using TaskPulse.Api.Models;
using TaskPulse.Api.Services;

namespace TaskPulse.Api.Controllers;

// CSV in and out. Export is a plain GET (same visibility as the lists it mirrors); import needs a session, takes the
// file as the request body (text/csv) or as a multipart `file`, and answers with what happened per row.
[ApiController]
[Tags("Import / export")]
public sealed class TransferController(CsvTransferService transfer) : ControllerBase
{
    private const int MaxCsvBytes = 4 * 1024 * 1024;

    [HttpGet("api/tasks/export.csv", Name = "ExportTasks")]
    [EndpointSummary("Every task the same filters would list, as CSV (id, title, description, status, priority, dueAt, assigneeId, assigneeName, labels, createdAt, updatedAt).")]
    [Produces("text/csv")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> ExportTasks([FromQuery] TaskListQuery query, CancellationToken cancellationToken)
        => File(Encoding.UTF8.GetBytes(await transfer.ExportTasksAsync(query, cancellationToken)), "text/csv; charset=utf-8", $"tasks-{DateTime.UtcNow:yyyyMMdd-HHmm}.csv");

    [HttpPost("api/tasks/import", Name = "ImportTasks")]
    [Authorize]
    [EnableRateLimiting(RateLimits.Writes)]
    [RequestSizeLimit(MaxCsvBytes)]
    [EndpointSummary("Create or update tasks from CSV (title required; id updates an existing task; labels separated by |). Bad rows are reported, the rest go through.")]
    [ProducesResponseType<ImportResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ImportResult>> ImportTasks(CancellationToken cancellationToken)
    {
        var csv = await ReadCsvAsync(cancellationToken);
        if (csv is null)
        {
            return CsvProblem("Send the CSV as the request body (text/csv) or as a multipart field named 'file'.");
        }

        try
        {
            return Ok(await transfer.ImportTasksAsync(csv, cancellationToken));
        }
        catch (ValidationException e)
        {
            return CsvProblem(e.Message);
        }
    }

    [HttpGet("api/catalog/{kind:regex(^[[a-z0-9]][[a-z0-9-]]{{0,63}}$)}/export.csv", Name = "ExportCatalog")]
    [EndpointSummary("A catalog kind as CSV (code, label, parents, attributes, sort, updatedAt).")]
    [Produces("text/csv")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> ExportCatalog(string kind, CancellationToken cancellationToken)
        => File(Encoding.UTF8.GetBytes(await transfer.ExportCatalogAsync(kind, cancellationToken)), "text/csv; charset=utf-8", $"{kind}-{DateTime.UtcNow:yyyyMMdd-HHmm}.csv");

    [HttpPost("api/catalog/{kind:regex(^[[a-z0-9]][[a-z0-9-]]{{0,63}}$)}/import", Name = "ImportCatalog")]
    [Authorize]
    [EnableRateLimiting(RateLimits.Writes)]
    [RequestSizeLimit(MaxCsvBytes)]
    [EndpointSummary("Create or update a kind's items from CSV (label required; an existing code is updated; parents separated by |, attributes as JSON).")]
    [ProducesResponseType<ImportResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ImportResult>> ImportCatalog(string kind, CancellationToken cancellationToken)
    {
        var csv = await ReadCsvAsync(cancellationToken);
        if (csv is null)
        {
            return CsvProblem("Send the CSV as the request body (text/csv) or as a multipart field named 'file'.");
        }

        try
        {
            return Ok(await transfer.ImportCatalogAsync(kind, csv, cancellationToken));
        }
        catch (ValidationException e)
        {
            return CsvProblem(e.Message);
        }
    }

    private async Task<string?> ReadCsvAsync(CancellationToken cancellationToken)
    {
        if (Request.HasFormContentType)
        {
            var form = await Request.ReadFormAsync(cancellationToken);
            var file = form.Files.GetFile("file") ?? form.Files.FirstOrDefault();
            if (file is null || file.Length == 0)
            {
                return null;
            }

            using var reader = new StreamReader(file.OpenReadStream(), Encoding.UTF8);
            return await reader.ReadToEndAsync(cancellationToken);
        }

        using var body = new StreamReader(Request.Body, Encoding.UTF8);
        var text = await body.ReadToEndAsync(cancellationToken);
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private ActionResult CsvProblem(string message)
        => ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]> { ["csv"] = [message] }));
}
