using System.Text.Json.Serialization;
using TaskPulse.Api.Data;
using TaskPulse.Api.Infrastructure;
using TaskPulse.Api.Repositories;
using TaskPulse.Api.Services;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options =>
{
    options.IncludeScopes = true;
    options.UseUtcTimestamp = true;
    options.TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z' ";
});

builder.Services
    .AddOptions<ApiOptions>()
    .Bind(builder.Configuration.GetSection(ApiOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services
    .AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.Configure<ApiBehaviorOptions>(options =>
    options.InvalidModelStateResponseFactory = ValidationProblemResponse.Create);

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<BadHttpRequestExceptionHandler>();
builder.Services.AddOpenApi();

var connectionString = DatabaseUrl.ToNpgsql(builder.Configuration["DATABASE_URL"])
    ?? builder.Configuration.GetConnectionString("Tasks")
    ?? throw new InvalidOperationException("Neither DATABASE_URL nor ConnectionStrings:Tasks is configured.");
builder.Services.AddDbContext<TasksDbContext>(options =>
    options.UseNpgsql(connectionString, npgsql => npgsql.EnableRetryOnFailure(maxRetryCount: 3)));

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<ITaskRepository, PostgresTaskRepository>();
builder.Services.AddScoped<ITaskService, TaskService>();

builder.Services.AddHealthChecks()
    .AddCheck<TaskStoreHealthCheck>("task-store", tags: ["ready"]);

builder.Services.Configure<ForwardedHeadersOptions>(options =>
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto);

builder.Services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromSeconds(10));

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<TasksDbContext>();
    await db.Database.MigrateAsync();
    var connection = db.Database.GetDbConnection();
    app.Logger.LogInformation("Database ready: {Database} on {DataSource}", connection.Database, connection.DataSource);
}

app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseMiddleware<CorrelationIdMiddleware>();

app.MapOpenApi();

app.MapHealthChecks("/health", new() { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new() { Predicate = check => check.Tags.Contains("ready") });

app.MapGet("/", () => Results.Redirect("/openapi/v1.json")).ExcludeFromDescription();
app.MapControllers();

await app.RunAsync();

public partial class Program;
