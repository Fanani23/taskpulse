using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace TaskPulse.Realtime.Infrastructure;

public sealed record SocketIdentity(string Sub, string? Email, IReadOnlyList<string> Roles)
{
    // What other clients see: the email when the token carries one, otherwise the subject id.
    public string Actor => Email ?? Sub;
}

// Validates the access token part A (express-template) issues, with the same rules TaskPulse.Api uses for its
// bearer scheme: HS256 with the shared secret, sub/roles/user_meta read as plain claims.
public sealed class TokenValidator(IOptions<WsOptions> options)
{
    private static readonly JsonWebTokenHandler Handler = new() { MapInboundClaims = false };

    public bool Enabled => !string.IsNullOrEmpty(options.Value.JwtSecret);

    public async Task<SocketIdentity?> ValidateAsync(string? token)
    {
        if (!Enabled || string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var settings = options.Value;
        var result = await Handler.ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.JwtSecret!.PadRight(32, '\0'))),
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            ValidateIssuer = !string.IsNullOrEmpty(settings.JwtIssuer),
            ValidIssuer = settings.JwtIssuer,
            ValidateAudience = !string.IsNullOrEmpty(settings.JwtAudience),
            ValidAudience = settings.JwtAudience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
        });

        if (!result.IsValid || result.SecurityToken is not JsonWebToken jwt)
        {
            return null;
        }

        var sub = jwt.Subject;
        if (string.IsNullOrEmpty(sub))
        {
            return null;
        }

        return new SocketIdentity(sub, ReadEmail(jwt), ReadRoles(jwt));
    }

    private static string? ReadEmail(JsonWebToken jwt)
    {
        if (jwt.TryGetClaim("email", out var email) && !string.IsNullOrEmpty(email.Value))
        {
            return email.Value;
        }

        if (!jwt.TryGetClaim("user_meta", out var meta) || string.IsNullOrEmpty(meta.Value))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(meta.Value);
            return doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("email", out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static IReadOnlyList<string> ReadRoles(JsonWebToken jwt)
    {
        var roles = new List<string>();
        foreach (var claim in jwt.Claims.Where(c => c.Type == "roles"))
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

        return roles;
    }
}
