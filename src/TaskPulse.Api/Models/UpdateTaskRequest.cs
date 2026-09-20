using System.ComponentModel.DataAnnotations;

namespace TaskPulse.Api.Models;

public sealed record UpdateTaskRequest
{
    [Required(ErrorMessage = "Title is required.")]
    [StringLength(TaskLimits.TitleMaxLength, ErrorMessage = "Title must be at most {1} characters.")]
    public string? Title { get; init; }

    [StringLength(TaskLimits.DescriptionMaxLength, ErrorMessage = "Description must be at most {1} characters.")]
    public string? Description { get; init; }

    [Required(ErrorMessage = "Status is required (Todo, InProgress or Done).")]
    public TaskItemStatus? Status { get; init; }

    public TaskPriority? Priority { get; init; }

    public DateTimeOffset? DueAt { get; init; }

    // A user id from part A (/api/users); AssigneeName is what the UI shows, stored so the list needs no join.
    [StringLength(64)]
    public string? AssigneeId { get; init; }

    [StringLength(TaskLimits.AssigneeMaxLength)]
    public string? AssigneeName { get; init; }

    [MaxLength(TaskLimits.LabelsMax, ErrorMessage = "At most {1} labels.")]
    public string[]? Labels { get; init; }
}
