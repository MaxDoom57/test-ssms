using TunnelManager.Worker.Services;

var builder = WebApplication.CreateBuilder(args);

// ─── Services ──────────────────────────────────────────────────────────────
builder.Services.AddSingleton<TunnelProcessService>();
builder.Services.AddHostedService<TunnelManagerService>();
builder.Services.AddHttpClient();

var app = builder.Build();

// ─── Health Endpoint (public — no auth required) ───────────────────────────
app.MapGet("/health", (TunnelProcessService processService) =>
{
    var statuses = processService.GetAllStatuses();

    return Results.Ok(new
    {
        status = "healthy",
        tunnelCount = statuses.Count,
        runningSince = DateTime.UtcNow, // approximation; exact value tracked in TunnelManagerService
        tunnels = statuses.Select(s => new
        {
            hostname = s.Hostname,
            port = s.LocalPort,
            isRunning = s.IsRunning,
            restarts = s.RestartCount
        })
    });
});

app.Run();
