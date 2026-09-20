using TaskPulse.Api.Models;
using TaskPulse.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace TaskPulse.Api.Controllers;

[ApiController]
[Route("api/tasks")]
[Tags("Tasks")]
public sealed class TasksController(ITaskService tasks) : ControllerBase
{
    [HttpGet(Name = "ListTasks")]
    [EndpointSummary("List tasks (paged; optional status filter and q text search on title and description).")]
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
    [EndpointSummary("Get a single task.")]
    [ProducesResponseType<TaskItem>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TaskItem>> Get(Guid id, CancellationToken cancellationToken)
        => await tasks.GetAsync(id, cancellationToken) is { } item ? Ok(item) : NotFound();

    [HttpPost(Name = "CreateTask")]
    [EndpointSummary("Create a task (status starts as Todo).")]
    [ProducesResponseType<TaskItem>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<TaskItem>> Create(CreateTaskRequest request, CancellationToken cancellationToken)
    {
        var item = await tasks.CreateAsync(request, cancellationToken);
        return Created(Url.RouteUrl("GetTask", new { id = item.Id })!, item);
    }

    [HttpPut("{id:guid}", Name = "UpdateTask")]
    [EndpointSummary("Replace title, description and status of a task.")]
    [ProducesResponseType<TaskItem>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TaskItem>> Update(Guid id, UpdateTaskRequest request, CancellationToken cancellationToken)
        => await tasks.UpdateAsync(id, request, cancellationToken) is { } item ? Ok(item) : NotFound();

    [HttpDelete("{id:guid}", Name = "DeleteTask")]
    [EndpointSummary("Delete a task.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
        => await tasks.DeleteAsync(id, cancellationToken) ? NoContent() : NotFound();
}
