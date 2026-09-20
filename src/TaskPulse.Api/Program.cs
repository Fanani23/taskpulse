using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Prometheus;
using TaskPulse.Api.Data;
using TaskPulse.Api.Infrastructure;
using TaskPulse.Api.Repositories;
using TaskPulse.Api.Services;

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

builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
{
    var origins = builder.Configuration.GetSection("Api:AllowedOrigins").Get<string[]>() ?? [];
    if (origins.Length > 0)
    {
        policy.WithOrigins(origins)
            .WithMethods("GET", "POST", "PUT", "DELETE")
            .WithHeaders("Content-Type", "Authorization", "If-Match")
            .WithExposedHeaders("Location", "ETag", "X-Total-Count", "X-Page", "X-Page-Size", "Retry-After");
    }
}));
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<BadHttpRequestExceptionHandler>();
builder.Services.AddOpenApi(options => options.AddDocumentTransformer((document, _, _) =>
{
    document.Components ??= new OpenApiComponents();
    document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
    document.Components.SecuritySchemes["bearer"] = new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Description = "The access token issued by the Vue + Express sign-in (HS256, same secret). Required for every POST/PUT/DELETE.",
    };
    return Task.CompletedTask;
}));

// Writes are authenticated with the access token that part A (express-template) issues: HS256 with the shared secret.
// sub/roles/user_meta are read as plain claims (no inbound claim-type mapping) so the audit trail sees the same
// identity the Vue app shows.
var jwtSecret = builder.Configuration["Api:JwtSecret"] ?? "";
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret.PadRight(32, '\0'))),
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            ValidateIssuer = !string.IsNullOrEmpty(builder.Configuration["Api:JwtIssuer"]),
            ValidIssuer = builder.Configuration["Api:JwtIssuer"],
            ValidateAudience = !string.IsNullOrEmpty(builder.Configuration["Api:JwtAudience"]),
            ValidAudience = builder.Configuration["Api:JwtAudience"],
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = "sub",
            RoleClaimType = "roles",
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();

// Fixed window per client address on every write; reads are not limited. 429 carries Retry-After.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.Headers.RetryAfter = "60";
        await context.HttpContext.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = StatusCodes.Status429TooManyRequests,
            Title = "Too many writes from this address — try again in a minute.",
            Type = "https://tools.ietf.org/html/rfc6585#section-4",
        }, options: null, contentType: "application/problem+json", cancellationToken);
    };
    options.AddPolicy(RateLimits.Writes, context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = builder.Configuration.GetValue("Api:WritesPerMinute", 120),
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        }));
});

var connectionString = DatabaseUrl.ToNpgsql(builder.Configuration["DATABASE_URL"])
    ?? builder.Configuration.GetConnectionString("Tasks")
    ?? throw new InvalidOperationException("Neither DATABASE_URL nor ConnectionStrings:Tasks is configured.");
builder.Services.AddDbContext<TasksDbContext>(options =>
    options.UseNpgsql(connectionString, npgsql => npgsql.EnableRetryOnFailure(maxRetryCount: 3)));

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<ITaskRepository, PostgresTaskRepository>();
builder.Services.AddScoped<ITaskService, TaskService>();
builder.Services.AddScoped<ICatalogRepository, PostgresCatalogRepository>();
builder.Services.AddScoped<ICatalogService, CatalogService>();
builder.Services.AddScoped<CsvTransferService>();
builder.Services.AddScoped<IPreferencesRepository, PostgresPreferencesRepository>();
builder.Services.AddScoped<IPreferencesService, PreferencesService>();
builder.Services.AddScoped<IUploadRepository, PostgresUploadRepository>();
builder.Services.AddSingleton<IUploadStore, DiskUploadStore>();
builder.Services.AddScoped<IUploadService, UploadService>();
builder.Services.AddScoped<IAuditRepository, PostgresAuditRepository>();
builder.Services.AddScoped<IAuditService, AuditService>();
builder.Services.AddSingleton<ChangePublisher>();
builder.Services.AddSingleton<IChangePublisher>(sp => sp.GetRequiredService<ChangePublisher>());
builder.Services.AddHttpClient(ChangeForwarder.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(3));
builder.Services.AddHostedService<ChangeForwarder>();

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
app.UseHttpMetrics();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapOpenApi();

app.MapHealthChecks("/health", new() { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new() { Predicate = check => check.Tags.Contains("ready") });
app.MapMetrics("/metrics");

app.MapGet("/", () => Results.Redirect("/openapi/v1.json")).ExcludeFromDescription();
app.MapControllers();

await app.RunAsync();

public partial class Program;
