using Microsoft.AspNetCore.Mvc;
using TaskPulse.Realtime.Services;

namespace TaskPulse.Realtime.Controllers;

[ApiController]
[Route("stats")]
public sealed class StatsController(ConnectionManager connections) : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok(connections.GetStats());
}
