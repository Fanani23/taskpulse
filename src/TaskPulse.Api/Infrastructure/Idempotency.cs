using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TaskPulse.Api.Data;

namespace TaskPulse.Api.Infrastructure;

// Safe retries for creates: a client that sends `Idempotency-Key: <its own id>` on a POST gets the first response
// back on every repeat (same status, body and Location, plus `Idempotent-Replayed: true`) instead of a second row.
// The key is scoped to the caller and the route; reusing it with a different body is 422, and two requests racing on
// the same key get 409 for the one that lost. Keys are kept in the database (so they survive restarts and are shared
// by every API node) for 24 hours. Without the header nothing changes.
[AttributeUsage(AttributeTargets.Method)]
public sealed class IdempotentAttribute : Attribute, IFilterFactory
{
    public bool IsReusable => false; // the filter holds the request's DbContext, so one instance per request

    public IFilterMetadata CreateInstance(IServiceProvider serviceProvider) => ActivatorUtilities.CreateInstance<IdempotencyFilter>(serviceProvider);
}

public static class IdempotencyLimits
{
    public const string Header = "Idempotency-Key";
    public const string ReplayedHeader = "Idempotent-Replayed";
    public const int KeyMaxLength = 128;
    public static readonly TimeSpan KeepFor = TimeSpan.FromHours(24);
}

public sealed class IdempotencyFilter(TasksDbContext db, ICurrentUser user, TimeProvider clock, IOptions<JsonOptions> json, ILogger<IdempotencyFilter> logger) : IAsyncActionFilter
{
    private static DateTime _lastSweepUtc = DateTime.MinValue;

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var key = context.HttpContext.Request.Headers[IdempotencyLimits.Header].ToString().Trim();
        if (key.Length == 0)
        {
            await next();
            return;
        }

        if (key.Length > IdempotencyLimits.KeyMaxLength)
        {
            context.Result = Problem(context, StatusCodes.Status400BadRequest, $"{IdempotencyLimits.Header} must be at most {IdempotencyLimits.KeyMaxLength} characters.");
            return;
        }

        var cancellationToken = context.HttpContext.RequestAborted;
        var scope = $"{user.Sub ?? "anon"}|{context.HttpContext.Request.Method} {context.HttpContext.Request.Path}";
        var fingerprint = Fingerprint(context.ActionArguments, context.HttpContext.Request);
        var now = clock.GetUtcNow().UtcDateTime;
        await SweepAsync(now, cancellationToken);

        var pending = new IdempotencyKeyEntity { Scope = scope, Key = key, Fingerprint = fingerprint, CreatedAtUtc = now };
        db.IdempotencyKeys.Add(pending);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            db.Entry(pending).State = EntityState.Detached;
            var existing = await db.IdempotencyKeys.AsNoTracking().SingleOrDefaultAsync(k => k.Scope == scope && k.Key == key, cancellationToken);
            if (existing is null)
            {
                throw;
            }

            if (existing.Fingerprint != fingerprint)
            {
                context.Result = Problem(context, StatusCodes.Status422UnprocessableEntity, $"{IdempotencyLimits.Header} '{key}' was already used for a different request.");
                return;
            }

            if (existing.Status is null)
            {
                context.Result = Problem(context, StatusCodes.Status409Conflict, $"A request with {IdempotencyLimits.Header} '{key}' is still being processed.");
                return;
            }

            context.HttpContext.Response.Headers[IdempotencyLimits.ReplayedHeader] = "true";
            if (existing.Location is { } storedLocation)
            {
                context.HttpContext.Response.Headers.Location = storedLocation;
            }

            context.Result = new ContentResult { StatusCode = existing.Status, Content = existing.Body, ContentType = existing.ContentType };
            return;
        }

        ActionExecutedContext executed;
        try
        {
            executed = await next();
        }
        catch
        {
            await ForgetAsync(pending);
            throw;
        }

        if (executed.Exception is not null && !executed.ExceptionHandled)
        {
            await ForgetAsync(pending);
            return;
        }

        var (status, body, contentType, location) = Capture(executed.Result, context.HttpContext.Response);
        if (status is null || status >= 500)
        {
            await ForgetAsync(pending); // nothing worth replaying
            return;
        }

        pending.Status = status;
        pending.Body = body;
        pending.ContentType = contentType;
        pending.Location = location;
        await db.SaveChangesAsync(cancellationToken);
    }

    private (int? Status, string? Body, string? ContentType, string? Location) Capture(IActionResult? result, HttpResponse response)
    {
        var options = json.Value.JsonSerializerOptions;
        var location = response.Headers.Location.ToString();
        return result switch
        {
            CreatedResult c => (c.StatusCode ?? 201, Serialize(c.Value, options), "application/json; charset=utf-8", c.Location ?? location),
            ObjectResult o => (o.StatusCode ?? 200, Serialize(o.Value, options), o.Value is ProblemDetails ? "application/problem+json; charset=utf-8" : "application/json; charset=utf-8", NullIfEmpty(location)),
            StatusCodeResult s => (s.StatusCode, null, null, NullIfEmpty(location)),
            _ => (null, null, null, null),
        };
    }

    private static string? Serialize(object? value, JsonSerializerOptions options) => value is null ? null : JsonSerializer.Serialize(value, value.GetType(), options);

    private static string? NullIfEmpty(string value) => value.Length == 0 ? null : value;

    // The request as the action sees it (bound arguments, or the multipart fields and file names/sizes): the same
    // key with a different body is an error, not a replay.
    private string Fingerprint(IDictionary<string, object?> arguments, HttpRequest request)
    {
        var options = json.Value.JsonSerializerOptions;
        var builder = new StringBuilder();
        if (request.HasFormContentType)
        {
            foreach (var (name, values) in request.Form.OrderBy(f => f.Key, StringComparer.Ordinal))
            {
                builder.Append(name).Append('=').Append(values.ToString()).Append('\n');
            }

            foreach (var file in request.Form.Files)
            {
                builder.Append("file:").Append(file.Name).Append('=').Append(file.FileName).Append('/').Append(file.Length).Append('\n');
            }
        }
        else
        {
            foreach (var (name, value) in arguments.OrderBy(a => a.Key, StringComparer.Ordinal))
            {
                if (value is CancellationToken)
                {
                    continue;
                }

                builder.Append(name).Append('=').Append(value is null ? "null" : JsonSerializer.Serialize(value, value.GetType(), options)).Append('\n');
            }
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    private async Task ForgetAsync(IdempotencyKeyEntity pending)
    {
        try
        {
            db.IdempotencyKeys.Remove(pending);
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Idempotency key {Key} could not be released", pending.Key);
        }
    }

    // Expired keys go away lazily, at most once a minute across the process.
    private async Task SweepAsync(DateTime now, CancellationToken cancellationToken)
    {
        if (now - _lastSweepUtc < TimeSpan.FromMinutes(1))
        {
            return;
        }

        _lastSweepUtc = now;
        var cutoff = now - IdempotencyLimits.KeepFor;
        await db.IdempotencyKeys.Where(k => k.CreatedAtUtc < cutoff).ExecuteDeleteAsync(cancellationToken);
    }

    private static ObjectResult Problem(ActionExecutingContext context, int status, string title)
        => new(new ProblemDetails { Status = status, Title = title, Instance = context.HttpContext.Request.Path }) { StatusCode = status, ContentTypes = { "application/problem+json" } };
}
