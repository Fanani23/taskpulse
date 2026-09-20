using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;

namespace TaskPulse.Api.Tests;

public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public static readonly string AdminConnectionString =
        Environment.GetEnvironmentVariable("TASKPULSE_TEST_PG")
        ?? "Host=localhost;Port=5432;Database=postgres;Username=taskpulse_dev;Password=taskpulse_dev";

    private readonly bool _ownsDatabase;

    public ApiFactory() : this($"taskpulse_test_{Guid.NewGuid():N}", ownsDatabase: true) { }

    private ApiFactory(string databaseName, bool ownsDatabase)
    {
        DatabaseName = databaseName;
        _ownsDatabase = ownsDatabase;
        EnsureDatabaseExists(databaseName);
    }

    public string DatabaseName { get; }

    public static ApiFactory ForDatabase(string databaseName) => new(databaseName, ownsDatabase: false);

    public static string ConnectionStringFor(string databaseName)
        => new NpgsqlConnectionStringBuilder(AdminConnectionString) { Database = databaseName }.ConnectionString;

    public const string JwtSecret = "taskpulse-tests-secret-do-not-use-in-production";

    public string UploadDirectory => Path.Combine(Path.GetTempPath(), "taskpulse-tests", DatabaseName);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
        => builder
            .UseSetting("ConnectionStrings:Tasks", ConnectionStringFor(DatabaseName))
            .UseSetting("Api:UploadDirectory", UploadDirectory)
            .UseSetting("Api:JwtSecret", JwtSecret)
            .UseSetting("Api:WritesPerMinute", "1000");

    // A client that carries an access token shaped like the one express-template issues (sub, roles, user_meta.email).
    public HttpClient CreateClient(string sub, string email, params string[] roles)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", TestTokens.Issue(sub, email, roles));
        return client;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && _ownsDatabase)
        {
            DropDatabase(DatabaseName);
            if (Directory.Exists(UploadDirectory))
            {
                Directory.Delete(UploadDirectory, recursive: true);
            }
        }
    }

    private static void EnsureDatabaseExists(string name)
    {
        using var admin = new NpgsqlConnection(AdminConnectionString);
        admin.Open();
        using var exists = new NpgsqlCommand("SELECT 1 FROM pg_database WHERE datname = @n", admin);
        exists.Parameters.AddWithValue("n", name);
        if (exists.ExecuteScalar() is null)
        {
            using var create = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", admin);
            create.ExecuteNonQuery();
        }
    }

    public static void DropDatabase(string name)
    {
        NpgsqlConnection.ClearAllPools();
        using var admin = new NpgsqlConnection(AdminConnectionString);
        admin.Open();
        using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)", admin);
        drop.ExecuteNonQuery();
    }
}
