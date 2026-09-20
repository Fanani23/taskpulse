using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;

namespace TaskPulse.Api.Tests;

public static class TestTokens
{
    public static string Issue(string sub, string email, string[] roles, string? secret = null, TimeSpan? lifetime = null)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes((secret ?? ApiFactory.JwtSecret).PadRight(32, '\0')));
        var now = DateTime.UtcNow;
        var expires = now.Add(lifetime ?? TimeSpan.FromMinutes(45));
        var token = new JwtSecurityToken(
            claims:
            [
                new Claim("sub", sub),
                new Claim("roles", JsonSerializer.Serialize(roles), JsonClaimValueTypes.JsonArray),
                new Claim("user_meta", JsonSerializer.Serialize(new { email }), JsonClaimValueTypes.Json),
                new Claim("scope", "openid profile email"),
            ],
            notBefore: expires > now ? now.AddMinutes(-1) : expires.AddMinutes(-15),
            expires: expires,
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
