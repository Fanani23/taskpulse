using System.Text.RegularExpressions;

namespace TaskPulse.Api.Infrastructure;

public static partial class FullText
{
    [GeneratedRegex(@"[\p{L}\p{N}]+")]
    private static partial Regex Words();

    // "PostgreSQL upgrade!" -> "postgresql:* & upgrade:*" - a safe tsquery: only word characters reach PostgreSQL, so
    // the user cannot inject operators, and every term is a prefix so partial words still match.
    public static string? ToPrefixQuery(string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return null;
        }

        var terms = Words().Matches(search).Select(m => m.Value.ToLowerInvariant()).Distinct().Take(8).ToList();
        return terms.Count == 0 ? null : string.Join(" & ", terms.Select(t => t + ":*"));
    }
}
