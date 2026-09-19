using Npgsql;

namespace TaskPulse.Api.Infrastructure;

public static class DatabaseUrl
{
    public static string? ToNpgsql(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        var uri = new Uri(url);
        var userInfo = uri.UserInfo.Split(':', 2);

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.Port > 0 ? uri.Port : 5432,
            Database = uri.AbsolutePath.TrimStart('/'),
            Username = Uri.UnescapeDataString(userInfo[0]),
            Password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : null,
        };

        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
        if (query["sslmode"] is { } sslMode && Enum.TryParse<SslMode>(sslMode, ignoreCase: true, out var parsed))
        {
            builder.SslMode = parsed;
        }

        return builder.ConnectionString;
    }
}
