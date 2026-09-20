using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TaskPulse.Api.Infrastructure;
using TaskPulse.Api.Models;
using TaskPulse.Api.Services;

namespace TaskPulse.Api.Controllers;

[ApiController]
[Route("api/catalog")]
[Tags("Catalog")]
public sealed class CatalogController(ICatalogService catalog) : ControllerBase
{
    private const string KindRoute = "{kind:regex(^[[a-z0-9]][[a-z0-9-]]{{0,63}}$)}";
    private const string ItemRoute = KindRoute + "/{code:regex(^[[a-z0-9]][[a-z0-9-]]{{0,63}}$)}";
    private const string SchemaRoute = KindRoute + "/_schema";

    [HttpGet(Name = "ListCatalogKinds")]
    [EndpointSummary("Kinds of reference data present, with item counts.")]
    [ProducesResponseType<IReadOnlyList<CatalogKindSummary>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<CatalogKindSummary>>> Kinds(CancellationToken cancellationToken)
        => Ok(await catalog.ListKindsAsync(cancellationToken));

    [HttpGet(KindRoute, Name = "ListCatalogItems")]
    [EndpointSummary("Items of one kind, ordered by sort then label; optional parent code, q text filter and page/pageSize (X-Total-Count header).")]
    [ProducesResponseType<IReadOnlyList<CatalogItem>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<CatalogItem>>> List(string kind, [FromQuery] CatalogListQuery query, CancellationToken cancellationToken)
    {
        var (items, total, page, pageSize) = await catalog.ListAsync(kind, query, cancellationToken);
        Response.Headers["X-Total-Count"] = total.ToString();
        Response.Headers["X-Page"] = page.ToString();
        Response.Headers["X-Page-Size"] = pageSize.ToString();
        return Ok(items);
    }

    [HttpGet(ItemRoute, Name = "GetCatalogItem")]
    [EndpointSummary("One item by kind and code; answers with an ETag.")]
    [ProducesResponseType<CatalogItem>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CatalogItem>> Get(string kind, string code, CancellationToken cancellationToken)
        => await catalog.GetAsync(kind, code, cancellationToken) is { } item ? Tagged(item) : NotFound();

    // Who changed what on one item: the audit rows for it, with the field-level diff of every update.
    [HttpGet(ItemRoute + "/history", Name = "CatalogItemHistory")]
    [Authorize]
    [EndpointSummary("History of one item (newest first, ≤ 200): actor, action, and for updates the fields that changed.")]
    [ProducesResponseType<IReadOnlyList<AuditEntry>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IReadOnlyList<AuditEntry>>> History(string kind, string code, [FromServices] IAuditService audit, CancellationToken cancellationToken)
        => Ok(await audit.ListAsync(new AuditListQuery { Resource = CatalogService.Resource, Kind = kind, Target = code, Limit = AuditLimits.ListMax }, cancellationToken));

    [HttpPost(KindRoute, Name = "CreateCatalogItem")]
    [Authorize]
    [EnableRateLimiting(RateLimits.Writes)]
    [EndpointSummary("Create an item; the code is derived from the label when omitted. 409 when the code exists; 400 when the kind has a schema the attributes violate.")]
    [ProducesResponseType<CatalogItem>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CatalogItem>> Create(string kind, CreateCatalogItemRequest request, CancellationToken cancellationToken)
    {
        var result = await catalog.CreateAsync(kind, request, cancellationToken);
        return result.Outcome switch
        {
            CatalogWriteOutcome.Ok => Created(Url.RouteUrl("GetCatalogItem", new { kind, code = result.Item!.Code })!, result.Item),
            CatalogWriteOutcome.Conflict => Problem(statusCode: StatusCodes.Status409Conflict, title: "Code already exists in this kind."),
            CatalogWriteOutcome.SchemaViolation => SchemaProblem(result.Errors!),
            _ => ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                ["code"] = [$"A code could not be derived from the label; pass one matching {CatalogLimits.CodePattern}."],
            })),
        };
    }

    [HttpPut(ItemRoute, Name = "UpdateCatalogItem")]
    [Authorize]
    [EnableRateLimiting(RateLimits.Writes)]
    [EndpointSummary("Replace label, parents, attributes and sort of an item. Send If-Match with the ETag you read to refuse lost updates (412).")]
    [ProducesResponseType<CatalogItem>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status412PreconditionFailed)]
    public async Task<ActionResult<CatalogItem>> Update(string kind, string code, UpdateCatalogItemRequest request, CancellationToken cancellationToken)
    {
        var result = await catalog.UpdateAsync(kind, code, request, ETags.Expected(Request), cancellationToken);
        return result.Outcome switch
        {
            CatalogWriteOutcome.Ok => Tagged(result.Item!),
            CatalogWriteOutcome.SchemaViolation => SchemaProblem(result.Errors!),
            CatalogWriteOutcome.VersionMismatch => ETags.PreconditionFailed(this),
            _ => NotFound(),
        };
    }

    [HttpDelete(ItemRoute, Name = "DeleteCatalogItem")]
    [Authorize]
    [EnableRateLimiting(RateLimits.Writes)]
    [EndpointSummary("Delete an item and drop its code from every item's parents.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(string kind, string code, CancellationToken cancellationToken)
        => await catalog.DeleteAsync(kind, code, cancellationToken) ? NoContent() : NotFound();

    [HttpGet(SchemaRoute, Name = "GetCatalogSchema")]
    [EndpointSummary("The JSON Schema the attributes of this kind must satisfy, if one is set.")]
    [ProducesResponseType<CatalogSchema>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CatalogSchema>> GetSchema(string kind, CancellationToken cancellationToken)
        => await catalog.GetSchemaAsync(kind, cancellationToken) is { } schema ? Ok(schema) : NotFound();

    [HttpPut(SchemaRoute, Name = "PutCatalogSchema")]
    [Authorize(Roles = "Admin")]
    [EnableRateLimiting(RateLimits.Writes)]
    [EndpointSummary("Set the JSON Schema for a kind (Admin). Refused when an existing item would violate it.")]
    [ProducesResponseType<CatalogSchema>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<CatalogSchema>> PutSchema(string kind, PutCatalogSchemaRequest request, CancellationToken cancellationToken)
    {
        var (ok, errors, schema) = await catalog.PutSchemaAsync(kind, request, cancellationToken);
        return ok ? Ok(schema) : ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]> { ["schema"] = [.. errors] }));
    }

    [HttpDelete(SchemaRoute, Name = "DeleteCatalogSchema")]
    [Authorize(Roles = "Admin")]
    [EnableRateLimiting(RateLimits.Writes)]
    [EndpointSummary("Remove the schema of a kind (Admin).")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteSchema(string kind, CancellationToken cancellationToken)
        => await catalog.DeleteSchemaAsync(kind, cancellationToken) ? NoContent() : NotFound();

    private ActionResult<CatalogItem> Tagged(CatalogItem item)
    {
        ETags.Set(Response, item.Version);
        return Ok(item);
    }

    private ActionResult SchemaProblem(IReadOnlyList<string> errors)
        => ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]> { ["attributes"] = [.. errors] }));
}
