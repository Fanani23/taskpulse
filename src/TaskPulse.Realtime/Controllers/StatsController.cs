using Microsoft.AspNetCore.Mvc;
using TaskPulse.Realtime.Services;

namespace TaskPulse.Realtime.Controllers;

[ApiController]
[Route("stats")]
public sealed class StatsController(ConnectionManager connections, NodeBus nodes, ChangeStreamReader stream) : ControllerBase
{
    // This node's sockets, plus how it is wired to the others: which node this is, whether Redis is attached, the
    // broadcasts relayed in from other nodes and the change-stream entries fanned out.
    [HttpGet]
    public IActionResult Get() => Ok(connections.GetStats() with { Node = nodes.NodeId, Redis = nodes.Connected, RelayedBroadcasts = nodes.Relayed, StreamEventsDelivered = stream.Delivered });
}
