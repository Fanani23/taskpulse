using System.Security.Claims;
using System.Text.Json;

namespace TaskPulse.Api.Infrastructure;

public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    string? Sub { get; }

    string? Email { get; }

    IReadOnlyList<string> Roles { get; }

    bool IsAdmin { get; }

    // What the audit trail records: the email when the token carries one, otherwise the subject id.
    string? Actor { get; }

    // The preferences key the portal uses for this user ("user-<sub>").
    string? PreferencesKey { get; }
}

public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private const string AdminRole = "Admin";

    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public string? Sub => Principal?.FindFirstValue("sub") ?? Principal?.FindFirstValue(ClaimTypes.NameIdentifier);

    public string? Email => ReadEmail(Principal);

    public IReadOnlyList<string> Roles => Principal is null ? [] : ReadRoles(Principal);

    public bool IsAdmin => Roles.Contains(AdminRole, StringComparer.OrdinalIgnoreCase);

    public string? Actor => IsAuthenticated ? Email ?? Sub : null;

    public string? PreferencesKey => Sub is null ? null : "user-" + Sub.ToLowerInvariant();

    private static IReadOnlyList<string> ReadRoles(ClaimsPrincipal principal)
    {
        var roles = new List<string>();
        foreach (var claim in principal.FindAll("roles").Concat(principal.FindAll(ClaimTypes.Role)))
        {
            if (claim.Value.StartsWith('['))
            {
                try
                {
                    roles.AddRange(JsonSerializer.Deserialize<string[]>(claim.Value) ?? []);
                    continue;
                }
                catch (JsonException)
                {
                }
            }

            roles.Add(claim.Value);
        }

        return roles.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string? ReadEmail(ClaimsPrincipal? principal)
    {
        if (principal is null)
        {
            return null;
        }

        var direct = principal.FindFirstValue("email") ?? principal.FindFirstValue(ClaimTypes.Email);
        if (!string.IsNullOrEmpty(direct))
        {
            return direct;
        }

        var meta = principal.FindFirstValue("user_meta");
        if (string.IsNullOrEmpty(meta) || !meta.StartsWith('{'))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(meta);
            return doc.RootElement.TryGetProperty("email", out var email) && email.ValueKind == JsonValueKind.String ? email.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
