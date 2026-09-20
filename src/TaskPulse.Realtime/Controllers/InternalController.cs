using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using TaskPulse.Realtime.Infrastructure;
using TaskPulse.Realtime.Models;
using TaskPulse.Realtime.Services;

namespace TaskPulse.Realtime.Controllers;

// Change notifications from TaskPulse.Api. Only reachable from the same machine: nginx never proxies /internal,
// and a request whose remote address is not loopback is refused even if it gets here.
[ApiController]
[Route("internal")]
public sealed class InternalController(ConnectionManager connections, IOptions<WsOptions> options, ILogger<InternalController> logger) : ControllerBase
{
    [HttpPost("broadcast")]
    public async Task<IActionResult> Broadcast(ChangeNotification change, CancellationToken cancellationToken)
    {
        var remote = HttpContext.Connection.RemoteIpAddress;
        var token = options.Value.InternalToken;
        var tokenOk = !string.IsNullOrEmpty(token) && string.Equals(Request.Headers["X-Internal-Token"], token, StringComparison.Ordinal);
        var loopbackOk = (remote is null || System.Net.IPAddress.IsLoopback(remote)) && !Request.Headers.ContainsKey("X-Forwarded-For");
        if (!tokenOk && !loopbackOk)
        {
            return Problem(statusCode: StatusCodes.Status403Forbidden, title: "Internal endpoint: loopback only.");
        }

        if (string.IsNullOrWhiteSpace(change.Resource) || string.IsNullOrWhiteSpace(change.Action) || string.IsNullOrWhiteSpace(change.Id))
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "resource, action and id are required.");
        }

        logger.LogInformation("Change {Resource}/{Action} {Id} fanned out to {Recipients} connection(s)", change.Resource, change.Action, change.Id, connections.Count);
        await connections.BroadcastAsync(ServerMessage.Changed(change), exceptId: null, cancellationToken);
        return Accepted(new { delivered = connections.Count });
    }
}
