using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using TaskPulse.Api.Data;
using TaskPulse.Api.Infrastructure;
using TaskPulse.Api.Models;
using TaskPulse.Api.Services;

namespace TaskPulse.Api.Controllers;

// Outgoing webhooks, Admin-only: where change events are POSTed, signed with the hook's secret. The secret is
// write-only. Deliveries (last 50 per hook) show what was sent and how it went.
[ApiController]
[Route("api/webhooks")]
[Tags("Webhooks")]
[Authorize(Roles = "Admin")]
[EnableRateLimiting(RateLimits.Writes)]
public sealed class WebhooksController(TasksDbContext db, ICurrentUser user, IAuditService audit, TimeProvider clock) : ControllerBase
{
    [HttpGet(Name = "ListWebhooks")]
    [EndpointSummary("Every webhook with its last delivery status (secrets never included).")]
    [ProducesResponseType<IReadOnlyList<Webhook>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<Webhook>>> List(CancellationToken cancellationToken)
        => Ok((await db.Webhooks.AsNoTracking().OrderBy(h => h.CreatedAtUtc).ToListAsync(cancellationToken)).Select(h => h.ToItem()).ToList());

    [HttpPost(Name = "CreateWebhook")]
    [EndpointSummary("Register a URL. https only (http on loopback for development); resources = which change events, empty for all.")]
    [ProducesResponseType<Webhook>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<Webhook>> Create(WebhookRequest request, CancellationToken cancellationToken)
    {
        if (UrlProblem(request.Url!) is { } problem)
        {
            return problem;
        }

        var hook = new WebhookEntity
        {
            Id = Guid.NewGuid(),
            Url = request.Url!,
            Secret = request.Secret!,
            Resources = Normalize(request.Resources),
            Active = request.Active ?? true,
            Description = request.Description?.Trim(),
            CreatedAtUtc = clock.GetUtcNow().UtcDateTime,
            CreatedBy = user.Actor,
        };
        db.Webhooks.Add(hook);
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync("create", "webhook", hook.Id.ToString(), hook.Url, cancellationToken: cancellationToken);
        return CreatedAtRoute("GetWebhook", new { id = hook.Id }, hook.ToItem());
    }

    [HttpGet("{id:guid}", Name = "GetWebhook")]
    [ProducesResponseType<Webhook>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<Webhook>> Get(Guid id, CancellationToken cancellationToken)
    {
        var hook = await db.Webhooks.AsNoTracking().FirstOrDefaultAsync(h => h.Id == id, cancellationToken);
        return hook is null ? NotFound() : Ok(hook.ToItem());
    }

    [HttpPut("{id:guid}", Name = "UpdateWebhook")]
    [EndpointSummary("Change URL, secret, resources, active flag or description (only the fields sent).")]
    [ProducesResponseType<Webhook>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<Webhook>> Update(Guid id, WebhookUpdateRequest request, CancellationToken cancellationToken)
    {
        var hook = await db.Webhooks.FirstOrDefaultAsync(h => h.Id == id, cancellationToken);
        if (hook is null)
        {
            return NotFound();
        }

        if (request.Url is not null)
        {
            if (UrlProblem(request.Url) is { } problem)
            {
                return problem;
            }

            hook.Url = request.Url;
        }

        if (request.Secret is not null)
        {
            hook.Secret = request.Secret;
        }

        if (request.Resources is not null)
        {
            hook.Resources = Normalize(request.Resources);
        }

        if (request.Active is { } active)
        {
            hook.Active = active;
            if (active)
            {
                hook.ConsecutiveFailures = 0; // re-enabling gives a dead endpoint a fresh count
            }
        }

        if (request.Description is not null)
        {
            hook.Description = request.Description.Trim();
        }

        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync("update", "webhook", hook.Id.ToString(), hook.Url, cancellationToken: cancellationToken);
        return Ok(hook.ToItem());
    }

    [HttpDelete("{id:guid}", Name = "DeleteWebhook")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var hook = await db.Webhooks.FirstOrDefaultAsync(h => h.Id == id, cancellationToken);
        if (hook is null)
        {
            return NotFound();
        }

        db.Webhooks.Remove(hook);
        await db.WebhookDeliveries.Where(d => d.WebhookId == id).ExecuteDeleteAsync(cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync("delete", "webhook", id.ToString(), hook.Url, cancellationToken: cancellationToken);
        return NoContent();
    }

    [HttpGet("{id:guid}/deliveries", Name = "WebhookDeliveries")]
    [EndpointSummary("The last 50 deliveries: event, attempts, HTTP status or error.")]
    [ProducesResponseType<IReadOnlyList<WebhookDelivery>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<WebhookDelivery>>> Deliveries(Guid id, CancellationToken cancellationToken)
    {
        if (!await db.Webhooks.AnyAsync(h => h.Id == id, cancellationToken))
        {
            return NotFound();
        }

        var rows = await db.WebhookDeliveries.AsNoTracking().Where(d => d.WebhookId == id).OrderByDescending(d => d.Id).Take(WebhookLimits.DeliveriesKept).ToListAsync(cancellationToken);
        return Ok(rows.Select(d => d.ToItem()).ToList());
    }

    private static string[] Normalize(string[]? resources)
        => resources is null ? [] : resources.Select(r => r.Trim().ToLowerInvariant()).Where(r => r.Length > 0).Distinct().Take(WebhookLimits.MaxResources).ToArray();

    // https on the open internet; plain http only to loopback (a local receiver during development).
    private ActionResult? UrlProblem(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != "https" && !(uri.Scheme == "http" && uri.IsLoopback)))
        {
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]> { ["url"] = ["Use an https URL (http is allowed for 127.0.0.1 / localhost only)."] }));
        }

        return null;
    }
}
