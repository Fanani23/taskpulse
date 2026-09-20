using Microsoft.AspNetCore.Mvc;
using TaskPulse.Realtime.Services;

namespace TaskPulse.Realtime.Controllers;

[ApiController]
[Route("ws")]
public sealed class SocketController(WebSocketSession session) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Connect(CancellationToken cancellationToken)
    {
        if (!HttpContext.WebSockets.IsWebSocketRequest)
        {
            return BadRequest(new { error = "Expected a WebSocket upgrade request on /ws." });
        }

        using var socket = await HttpContext.WebSockets.AcceptWebSocketAsync();
        await session.RunAsync(socket, HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken);
        return new EmptyResult();
    }
}
