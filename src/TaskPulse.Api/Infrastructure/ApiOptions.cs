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

    [Required]
    public string UploadDirectory { get; set; } = "uploads";

    [Range(1024, 64 * 1024 * 1024)]
    public long MaxUploadBytes { get; set; } = 2 * 1024 * 1024;

    // The HS256 secret the Vue + Express side signs its access tokens with (express JWT_SECRET). Writes require a
    // token signed with it; reads stay open. Required so a box can never run with anonymous writes by accident.
    [Required(ErrorMessage = "Api:JwtSecret is required (the express JWT_SECRET, ≥ 16 characters).")]
    [MinLength(16, ErrorMessage = "Api:JwtSecret must be at least 16 characters.")]
    public string JwtSecret { get; set; } = "";

    public string? JwtIssuer { get; set; }

    public string? JwtAudience { get; set; }

    [Range(1, 10000)]
    public int WritesPerMinute { get; set; } = 120;

    // Where TaskPulse.Realtime accepts change notifications (loopback only). Empty disables the fan-out.
    public string? RealtimeInternalUrl { get; set; }

    // Sent as X-Internal-Token when the Realtime service is on another host (compose); empty on a single box.
    public string? RealtimeInternalToken { get; set; }
}
