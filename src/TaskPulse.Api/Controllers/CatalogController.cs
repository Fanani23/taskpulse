using Microsoft.AspNetCore.Mvc;
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

    [HttpGet(Name = "ListCatalogKinds")]
    [EndpointSummary("Kinds of reference data present, with item counts.")]
    [ProducesResponseType<IReadOnlyList<CatalogKindSummary>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<CatalogKindSummary>>> Kinds(CancellationToken cancellationToken)
        => Ok(await catalog.ListKindsAsync(cancellationToken));

    [HttpGet(KindRoute, Name = "ListCatalogItems")]
    [EndpointSummary("Items of one kind, ordered by sort then label; optional parent code and q text filter.")]
    [ProducesResponseType<IReadOnlyList<CatalogItem>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<CatalogItem>>> List(string kind, [FromQuery] CatalogListQuery query, CancellationToken cancellationToken)
        => Ok(await catalog.ListAsync(kind, query, cancellationToken));

    [HttpGet(ItemRoute, Name = "GetCatalogItem")]
    [EndpointSummary("One item by kind and code.")]
    [ProducesResponseType<CatalogItem>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CatalogItem>> Get(string kind, string code, CancellationToken cancellationToken)
        => await catalog.GetAsync(kind, code, cancellationToken) is { } item ? Ok(item) : NotFound();

    [HttpPost(KindRoute, Name = "CreateCatalogItem")]
    [EndpointSummary("Create an item; the code is derived from the label when omitted. 409 when the code exists.")]
    [ProducesResponseType<CatalogItem>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CatalogItem>> Create(string kind, CreateCatalogItemRequest request, CancellationToken cancellationToken)
    {
        var result = await catalog.CreateAsync(kind, request, cancellationToken);
        return result.Outcome switch
        {
            CatalogWriteOutcome.Ok => Created(Url.RouteUrl("GetCatalogItem", new { kind, code = result.Item!.Code })!, result.Item),
            CatalogWriteOutcome.Conflict => Problem(statusCode: StatusCodes.Status409Conflict, title: "Code already exists in this kind."),
            _ => ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                ["code"] = [$"A code could not be derived from the label; pass one matching {CatalogLimits.CodePattern}."],
            })),
        };
    }

    [HttpPut(ItemRoute, Name = "UpdateCatalogItem")]
    [EndpointSummary("Replace label, parents, attributes and sort of an item.")]
    [ProducesResponseType<CatalogItem>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CatalogItem>> Update(string kind, string code, UpdateCatalogItemRequest request, CancellationToken cancellationToken)
    {
        var result = await catalog.UpdateAsync(kind, code, request, cancellationToken);
        return result.Outcome == CatalogWriteOutcome.Ok ? Ok(result.Item) : NotFound();
    }

    [HttpDelete(ItemRoute, Name = "DeleteCatalogItem")]
    [EndpointSummary("Delete an item and drop its code from every item's parents.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(string kind, string code, CancellationToken cancellationToken)
        => await catalog.DeleteAsync(kind, code, cancellationToken) ? NoContent() : NotFound();
}
