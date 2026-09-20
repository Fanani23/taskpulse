using Microsoft.Extensions.Options;
using TaskPulse.Realtime.Infrastructure;
using TaskPulse.Realtime.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options =>
{
    options.IncludeScopes = true;
    options.UseUtcTimestamp = true;
    options.TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z' ";
});

builder.Services
    .AddOptions<WsOptions>()
    .Bind(builder.Configuration.GetSection(WsOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddControllers();
builder.Services.AddSingleton<ConnectionManager>();
builder.Services.AddSingleton<TokenValidator>();
builder.Services.AddSingleton<MessageRouter>();
builder.Services.AddSingleton<ChangeStreamReader>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<ChangeStreamReader>());
builder.Services.AddScoped<WebSocketSession>();
builder.Services.AddHealthChecks();
builder.Services.AddProblemDetails();

builder.Services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromSeconds(10));

var app = builder.Build();
var wsOptions = app.Services.GetRequiredService<IOptions<WsOptions>>().Value;

app.UseExceptionHandler();

app.UseDefaultFiles();
app.UseStaticFiles();

app.UseWebSockets(new WebSocketOptions
{
    KeepAliveInterval = TimeSpan.FromSeconds(wsOptions.KeepAliveIntervalSeconds),
});

app.MapHealthChecks("/health");
app.MapControllers();

app.Run();

public partial class Program;
