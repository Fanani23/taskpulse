using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using TaskPulse.Api.Infrastructure;
using TaskPulse.Api.Models;
using TaskPulse.Api.Services;

namespace TaskPulse.Api.Controllers;

[ApiController]
[Route("api/audit")]
[Tags("Audit")]
public sealed class AuditController(IAuditService audit, ICurrentUser user, IOptions<ApiOptions> options) : ControllerBase
{
    // The trail names people (emails, and for sign-in events addresses), so reading it needs a session; the sign-in
    // events themselves (resource=auth, account) are for Admins.
    [HttpGet(Name = "ListAudit")]
    [Authorize]
    [EndpointSummary("Who changed what, newest first (limit ≤ 200; optional resource = task | catalog | upload | auth | account).")]
    [ProducesResponseType<IReadOnlyList<AuditEntry>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<AuditEntry>>> List([FromQuery] AuditListQuery query, CancellationToken cancellationToken)
    {
        if (query.Resource is "auth" or "account" && !user.IsAdmin)
        {
            return Problem(statusCode: StatusCodes.Status403Forbidden, title: "Sign-in events are for administrators.");
        }

        var rows = await audit.ListAsync(query, cancellationToken);
        return Ok(user.IsAdmin ? rows : rows.Where(r => r.Resource is not ("auth" or "account")).ToList());
    }

    // Reported by part A (sign-ins, failures, lockouts, revocations): server to server, with the shared token.
    [HttpPost(Name = "RecordAudit")]
    [EndpointSummary("Record an event reported by another service (X-Internal-Token).")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Record(ExternalAuditEvent e, CancellationToken cancellationToken)
    {
        var token = options.Value.AuditIngestToken;
        if (string.IsNullOrEmpty(token) || !string.Equals(Request.Headers["X-Internal-Token"], token, StringComparison.Ordinal))
        {
            return Problem(statusCode: StatusCodes.Status403Forbidden, title: "Audit ingest needs the internal token.");
        }

        await audit.RecordExternalAsync(e, cancellationToken);
        return Accepted();
    }
}
