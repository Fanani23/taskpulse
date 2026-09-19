using Npgsql;
using TaskPulse.Api.Infrastructure;
using Xunit;

namespace TaskPulse.Api.Tests;

public sealed class DatabaseUrlTests
{
    [Fact]
    public void Converts_a_paas_style_url_including_escaped_password_and_sslmode()
    {
        var result = DatabaseUrl.ToNpgsql("postgres://app:p%40ss%3Aword@db.internal:5433/taskpulse?sslmode=require");

        var parsed = new NpgsqlConnectionStringBuilder(result);
        Assert.Equal("db.internal", parsed.Host);
        Assert.Equal(5433, parsed.Port);
        Assert.Equal("taskpulse", parsed.Database);
        Assert.Equal("app", parsed.Username);
        Assert.Equal("p@ss:word", parsed.Password);
        Assert.Equal(SslMode.Require, parsed.SslMode);
    }

    [Fact]
    public void Defaults_port_and_leaves_sslmode_alone_when_absent()
    {
        var parsed = new NpgsqlConnectionStringBuilder(DatabaseUrl.ToNpgsql("postgresql://u:p@host/db"));

        Assert.Equal(5432, parsed.Port);
        Assert.Equal(SslMode.Prefer, parsed.SslMode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Returns_null_when_unset(string? url)
        => Assert.Null(DatabaseUrl.ToNpgsql(url));
}
