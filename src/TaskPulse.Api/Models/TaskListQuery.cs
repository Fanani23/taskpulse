namespace TaskPulse.Api.Models;

public sealed record TaskListQuery
{
    public TaskItemStatus? Status { get; init; }

    public int? Page { get; init; }

    public int? PageSize { get; init; }
}
