using System.ComponentModel.DataAnnotations;

namespace TaskPulse.Api.Models;

public sealed record TaskListQuery
{
    public TaskItemStatus? Status { get; init; }

    [StringLength(TaskLimits.SearchMaxLength, ErrorMessage = "q must be at most {1} characters.")]
    public string? Q { get; init; }

    public bool IncludeDeleted { get; init; }

    public TaskPriority? Priority { get; init; }

    // A user id, or "me" for the caller
    [StringLength(64)]
    public string? Assignee { get; init; }

    [StringLength(TaskLimits.LabelMaxLength)]
    public string? Label { get; init; }

    // overdue | today | week | none
    [StringLength(16)]
    public string? Due { get; init; }

    public int? Page { get; init; }

    public int? PageSize { get; init; }
}
