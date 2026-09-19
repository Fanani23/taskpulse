using System.ComponentModel.DataAnnotations;

namespace TaskPulse.Api.Infrastructure;

public sealed class ApiOptions
{
    public const string SectionName = "Api";

    [Range(1, 500)]
    public int DefaultPageSize { get; set; } = 20;

    [Range(1, 1000)]
    public int MaxPageSize { get; set; } = 100;

    public string[] AllowedOrigins { get; set; } = [];
}
