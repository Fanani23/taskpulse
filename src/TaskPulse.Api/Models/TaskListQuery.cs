using System.ComponentModel.DataAnnotations;

namespace TaskPulse.Api.Models;

public sealed record TaskListQuery
{
    public TaskItemStatus? Status { get; init; }

    [StringLength(TaskLimits.SearchMaxLength, ErrorMessage = "q must be at most {1} characters.")]
    public string? Q { get; init; }

    public int? Page { get; init; }

    public int? PageSize { get; init; }
}
