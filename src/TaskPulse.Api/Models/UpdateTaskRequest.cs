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
}
