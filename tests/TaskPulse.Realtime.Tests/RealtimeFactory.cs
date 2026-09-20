using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.IdentityModel.Tokens;

namespace TaskPulse.Realtime.Tests;

// The server under test with the sign-in secret and small rate limits, so the tests can hit them quickly.
public sealed class RealtimeFactory : WebApplicationFactory<Program>
{
    public const string JwtSecret = "realtime-tests-secret-with-at-least-32-chars";
    public const int MessagesPerMinute = 10;
    public const int BroadcastsPerMinute = 2;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("WebSocket:JwtSecret", JwtSecret);
        builder.UseSetting("WebSocket:MessagesPerMinute", MessagesPerMinute.ToString());
        builder.UseSetting("WebSocket:BroadcastsPerMinute", BroadcastsPerMinute.ToString());
    }

    public static string IssueToken(string sub, string email, string[] roles, TimeSpan? lifetime = null, string? secret = null)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes((secret ?? JwtSecret).PadRight(32, '\0')));
        var now = DateTime.UtcNow;
        var expires = now.Add(lifetime ?? TimeSpan.FromMinutes(15));
        var token = new JwtSecurityToken(
            claims:
            [
                new Claim("sub", sub),
                new Claim("roles", JsonSerializer.Serialize(roles), JsonClaimValueTypes.JsonArray),
                new Claim("user_meta", JsonSerializer.Serialize(new { email }), JsonClaimValueTypes.Json),
            ],
            notBefore: expires > now ? now.AddMinutes(-1) : expires.AddMinutes(-15),
            expires: expires,
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
