using AspNetCoreRateLimit;
using Microsoft.EntityFrameworkCore;
using TunnelManager.ConfigApi.Data;
using TunnelManager.ConfigApi.Endpoints;
using TunnelManager.ConfigApi.Middleware;
using TunnelManager.ConfigApi.Services;

var builder = WebApplication.CreateBuilder(args);

// ─── Rate Limiting ─────────────────────────────────────────────────────────
builder.Services.AddMemoryCache();
builder.Services.Configure<IpRateLimitOptions>(options =>
{
    options.EnableEndpointRateLimiting = false;
    options.StackBlockedRequests = false;
    options.HttpStatusCode = 429;
    options.RealIpHeader = "X-Real-IP";
    options.GeneralRules = new List<RateLimitRule>
    {
        new RateLimitRule
        {
            Endpoint = "*",
            Period = "1m",
            Limit = 20
        }
    };
});
builder.Services.AddSingleton<IIpPolicyStore, MemoryCacheIpPolicyStore>();
builder.Services.AddSingleton<IRateLimitCounterStore, MemoryCacheRateLimitCounterStore>();
builder.Services.AddSingleton<IRateLimitConfiguration, RateLimitConfiguration>();
builder.Services.AddSingleton<IProcessingStrategy, AsyncKeyLockProcessingStrategy>();
builder.Services.AddInMemoryRateLimiting();

// ─── Database ──────────────────────────────────────────────────────────────
builder.Services.AddDbContext<TunnelDbContext>(options =>
    options.UseSqlServer(builder.Configuration["DATABASE_URL"]));

// ─── Application Services ──────────────────────────────────────────────────
builder.Services.AddScoped<IVaultService, VaultService>();
builder.Services.AddScoped<IAuditService, AuditService>();

// ─── HTTP Client ───────────────────────────────────────────────────────────
builder.Services.AddHttpClient();

var app = builder.Build();

// ─── Auto-migrate on startup ───────────────────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<TunnelDbContext>();
    if (db.Database.ProviderName != "Microsoft.EntityFrameworkCore.Sqlite")
    {
        db.Database.Migrate();
    }
}

// ─── Middleware Pipeline ───────────────────────────────────────────────────
app.UseIpRateLimiting();

// Security middleware — must be before route mappings
app.UseMiddleware<InternalKeyMiddleware>();

// ─── Endpoints ─────────────────────────────────────────────────────────────
app.MapTunnelEndpoints();

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }))
   .AllowAnonymous();

app.Run();

// Make Program accessible for WebApplicationFactory in tests
public partial class Program { }
