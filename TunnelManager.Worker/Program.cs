using TunnelManager.Worker.Services;

var builder = WebApplication.CreateBuilder(args);

// ─── Services ──────────────────────────────────────────────────────────────
builder.Services.AddSingleton<TunnelProcessService>();
builder.Services.AddHostedService<TunnelManagerService>();
builder.Services.AddHttpClient();

var app = builder.Build();

// Health endpoint so Render and UptimeRobot can ping it
app.MapGet("/health", () => Results.Ok(new
{
    status = "healthy",
    timestamp = DateTime.UtcNow
}));

app.Run();
