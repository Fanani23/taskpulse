namespace TaskPulse.Api.Repositories;

internal static class LikePatterns
{
    public const string EscapeCharacter = "\\";

    public static string Escape(string value)
        => value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
