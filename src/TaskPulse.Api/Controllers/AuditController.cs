using Microsoft.AspNetCore.Mvc;
using TaskPulse.Api.Models;
using TaskPulse.Api.Services;

namespace TaskPulse.Api.Controllers;

[ApiController]
[Route("api/audit")]
[Tags("Audit")]
public sealed class AuditController(IAuditService audit) : ControllerBase
{
    [HttpGet(Name = "ListAudit")]
    [EndpointSummary("Who changed what, newest first (limit ≤ 200; optional resource = task | catalog | upload).")]
    [ProducesResponseType<IReadOnlyList<AuditEntry>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<IReadOnlyList<AuditEntry>>> List([FromQuery] AuditListQuery query, CancellationToken cancellationToken)
        => Ok(await audit.ListAsync(query, cancellationToken));
}
