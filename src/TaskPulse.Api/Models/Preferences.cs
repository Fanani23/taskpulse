using System.ComponentModel.DataAnnotations;

namespace TaskPulse.Api.Models;

public static class PreferenceLimits
{
    public const int UserIdMaxLength = 64;
    public const int NicknameMaxLength = 40;
    public const string UserIdPattern = "^[A-Za-z0-9][A-Za-z0-9._@+-]{0,63}$";
    public static readonly string[] Themes = ["light", "dark", "system"];
}

public sealed record Preferences(string UserId, string Theme, string? Nickname, DateTimeOffset? UpdatedAt, bool Saved = true)
{
    public static Preferences Defaults(string userId) => new(userId, "system", null, null, Saved: false);
}

public sealed record UpsertPreferencesRequest
{
    [Required(ErrorMessage = "Theme is required (light, dark or system).")]
    [RegularExpression("^(light|dark|system)$", ErrorMessage = "Theme must be light, dark or system.")]
    public string? Theme { get; init; }

    [StringLength(PreferenceLimits.NicknameMaxLength, ErrorMessage = "Nickname must be at most {1} characters.")]
    public string? Nickname { get; init; }
}
