using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TaskPulse.Api.Infrastructure;
using TaskPulse.Api.Models;
using TaskPulse.Api.Services;

namespace TaskPulse.Api.Controllers;

[ApiController]
[Route("api/tasks")]
[Tags("Tasks")]
public sealed class TasksController(ITaskService tasks) : ControllerBase
{
    // ?page=&pageSize= is the offset view; the answer's nextCursor continues the same list by keyset (?cursor=) -
    // stable while rows are inserted or moved, and no OFFSET scan for deep pages.
    [HttpGet(Name = "ListTasks")]
    [EndpointSummary("List tasks (paged; optional status filter, q text search on title and description, includeDeleted).")]
    [ProducesResponseType<PagedResponse<TaskItem>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResponse<TaskItem>>> List([FromQuery] TaskListQuery query, CancellationToken cancellationToken)
        => Ok(await tasks.ListAsync(query, cancellationToken));

    [HttpGet("stats", Name = "TaskStats")]
    [EndpointSummary("Counts by status, completion rate, and created/done per day for the last N days (default 14, max 90).")]
    [ProducesResponseType<TaskStats>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<TaskStats>> Stats([FromQuery] TaskStatsQuery query, CancellationToken cancellationToken)
        => Ok(await tasks.GetStatsAsync(query, cancellationToken));

    [HttpGet("{id:guid}", Name = "GetTask")]
    [EndpointSummary("Get a single task; answers with an ETag built from its row version.")]
    [ProducesResponseType<TaskItem>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TaskItem>> Get(Guid id, [FromQuery] bool includeDeleted, CancellationToken cancellationToken)
    {
        var item = await tasks.GetAsync(id, includeDeleted, cancellationToken);
        if (item is null)
        {
            return NotFound();
        }

        ETags.Set(Response, item.Version);
        return Ok(item);
    }

    [HttpPost(Name = "CreateTask")]
    [Authorize]
    [Idempotent]
    [EnableRateLimiting(RateLimits.Writes)]
    [EndpointSummary("Create a task (status starts as Todo). Requires a bearer token from the Vue + Express sign-in. Send Idempotency-Key to make a retry return the first result instead of a second task.")]
    [ProducesResponseType<TaskItem>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<TaskItem>> Create(CreateTaskRequest request, CancellationToken cancellationToken)
    {
        var item = await tasks.CreateAsync(request, cancellationToken);
        return Created(Url.RouteUrl("GetTask", new { id = item.Id })!, item);
    }

    [HttpPut("{id:guid}", Name = "UpdateTask")]
    [Authorize]
    [EnableRateLimiting(RateLimits.Writes)]
    [EndpointSummary("Replace title, description and status. Send If-Match with the ETag you read to refuse lost updates (412).")]
    [ProducesResponseType<TaskItem>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status412PreconditionFailed)]
    public async Task<ActionResult<TaskItem>> Update(Guid id, UpdateTaskRequest request, CancellationToken cancellationToken)
    {
        var result = await tasks.UpdateAsync(id, request, ETags.Expected(Request), cancellationToken);
        return result.Outcome switch
        {
            WriteOutcome.Ok => Tagged(result.Item!),
            WriteOutcome.VersionMismatch => ETags.PreconditionFailed(this),
            _ => NotFound(),
        };
    }

    [HttpDelete("{id:guid}", Name = "DeleteTask")]
    [Authorize]
    [EnableRateLimiting(RateLimits.Writes)]
    [EndpointSummary("Soft-delete a task (restorable). ?permanent=true purges it and needs the Admin role.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, [FromQuery] bool permanent, CancellationToken cancellationToken)
    {
        var result = await tasks.DeleteAsync(id, permanent, cancellationToken);
        return result.Outcome switch
        {
            WriteOutcome.Ok => NoContent(),
            WriteOutcome.Forbidden => Problem(statusCode: StatusCodes.Status403Forbidden, title: "Only an Admin can purge a task."),
            _ => NotFound(),
        };
    }

    [HttpGet("{id:guid}/history", Name = "TaskHistory")]
    [Authorize]
    [EndpointSummary("History of one task (newest first, ≤ 200): actor, action, and for updates the fields that changed.")]
    [ProducesResponseType<IReadOnlyList<AuditEntry>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IReadOnlyList<AuditEntry>>> History(Guid id, [FromServices] IAuditService audit, CancellationToken cancellationToken)
        => Ok(await audit.ListAsync(new AuditListQuery { Resource = TaskService.Resource, Target = id.ToString(), Limit = AuditLimits.ListMax }, cancellationToken));

    [HttpPost("{id:guid}/restore", Name = "RestoreTask")]
    [Authorize]
    [EnableRateLimiting(RateLimits.Writes)]
    [EndpointSummary("Bring a soft-deleted task back.")]
    [ProducesResponseType<TaskItem>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TaskItem>> Restore(Guid id, CancellationToken cancellationToken)
    {
        var result = await tasks.RestoreAsync(id, cancellationToken);
        return result.Outcome == WriteOutcome.Ok ? Tagged(result.Item!) : NotFound();
    }

    private ActionResult<TaskItem> Tagged(TaskItem item)
    {
        ETags.Set(Response, item.Version);
        return Ok(item);
    }
}
