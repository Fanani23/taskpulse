using TaskPulse.Api.Models;

namespace TaskPulse.Api.Data;

public sealed class PreferenceEntity
{
    public required string UserId { get; set; }

    public required string Theme { get; set; }

    public string? Nickname { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public Preferences ToItem() => new(
        UserId,
        Theme,
        Nickname,
        new DateTimeOffset(DateTime.SpecifyKind(UpdatedAtUtc, DateTimeKind.Utc)));

    public static PreferenceEntity From(Preferences item) => new()
    {
        UserId = item.UserId,
        Theme = item.Theme,
        Nickname = item.Nickname,
        UpdatedAtUtc = item.UpdatedAt.UtcDateTime,
    };

    public void Apply(Preferences item)
    {
        Theme = item.Theme;
        Nickname = item.Nickname;
        UpdatedAtUtc = item.UpdatedAt.UtcDateTime;
    }
}
