using System.Net.Http.Json;
using TunnelManager.Shared.Models;

namespace TunnelManager.Worker.Services;

public class TunnelManagerService : BackgroundService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TunnelProcessService _processService;
    private readonly ILogger<TunnelManagerService> _logger;
    private readonly string _configApiUrl;
    private readonly string _internalKey;
    private readonly DateTime _startedAt = DateTime.UtcNow;

    private const int MaxStartupRetries = 5;
    private const int RetryDelaySeconds = 10;
    private const int MonitoringIntervalSeconds = 30;

    public TunnelManagerService(
        IHttpClientFactory httpClientFactory,
        TunnelProcessService processService,
        ILogger<TunnelManagerService> logger,
        IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;
        _processService = processService;
        _logger = logger;

        _configApiUrl = configuration["CONFIG_API_URL"]
            ?? throw new InvalidOperationException("CONFIG_API_URL environment variable is not set.");

        _internalKey = configuration["INTERNAL_CONFIG_API_KEY"]
            ?? throw new InvalidOperationException("INTERNAL_CONFIG_API_KEY environment variable is not set.");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("TunnelManagerService starting...");

        // ── STARTUP: Load and spawn all tunnels ────────────────────────────
        var tunnels = await FetchTunnelsWithRetriesAsync(stoppingToken);
        if (tunnels == null)
        {
            _logger.LogCritical("Could not fetch tunnel configuration after {MaxRetries} retries. Exiting.",
                MaxStartupRetries);
            throw new InvalidOperationException("Config API unavailable on startup — giving up.");
        }

        var spawnTasks = tunnels.Select(t =>
            _processService.SpawnTunnelAsync(t.Config, t.Credential));

        var results = await Task.WhenAll(spawnTasks);
        var startedCount = results.Count(r => r);

        _logger.LogInformation("Started {StartedCount}/{Total} tunnels on startup.",
            startedCount, tunnels.Count);

        // ── MONITORING LOOP ────────────────────────────────────────────────
        while (!stoppingToken.IsCancellationRequested)
        {
            // Self ping every 5 minutes to prevent Render free tier spin down
            if (DateTime.UtcNow.Minute % 5 == 0)
            {
                try
                {
                    using var pingClient = _httpClientFactory.CreateClient();
                    await pingClient.GetAsync("http://localhost:8080/health", stoppingToken);
                    _logger.LogInformation("Self-ping successful");
                }
                catch { /* ignore ping failures */ }
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(MonitoringIntervalSeconds), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            await RunHealthCheckCycleAsync(stoppingToken);
        }

        // ── GRACEFUL SHUTDOWN ──────────────────────────────────────────────
        _logger.LogInformation("TunnelManagerService shutting down — stopping all tunnels...");

        var allStatuses = _processService.GetAllStatuses();
        var stopTasks = allStatuses.Select(s => _processService.StopTunnelAsync(s.Hostname));
        await Task.WhenAll(stopTasks);

        _logger.LogInformation("TunnelManagerService shutdown complete. All tunnel processes stopped.");
    }

    private async Task<List<TunnelWithCredential>?> FetchTunnelsWithRetriesAsync(CancellationToken ct)
    {
        for (int attempt = 1; attempt <= MaxStartupRetries; attempt++)
        {
            try
            {
                var tunnels = await FetchTunnelsAsync(ct);
                return tunnels;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    "Attempt {Attempt}/{Max} to fetch tunnels failed: {Message}",
                    attempt, MaxStartupRetries, ex.Message);

                if (attempt < MaxStartupRetries)
                {
                    _logger.LogInformation("Retrying in {Delay} seconds...", RetryDelaySeconds);
                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(RetryDelaySeconds), ct);
                    }
                    catch (OperationCanceledException)
                    {
                        return null;
                    }
                }
            }
        }

        return null;
    }

    private async Task<List<TunnelWithCredential>> FetchTunnelsAsync(CancellationToken ct)
    {
        using var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Add("x-internal-key", _internalKey);

        var response = await client.GetAsync($"{_configApiUrl}/tunnels", ct);
        response.EnsureSuccessStatusCode();

        var raw = await response.Content.ReadFromJsonAsync<List<TunnelWithCredentialDto>>(
            cancellationToken: ct);

        if (raw == null)
            return new List<TunnelWithCredential>();

        return raw.Select(dto => new TunnelWithCredential
        {
            Config = new TunnelConfig
            {
                Id = dto.Id,
                Hostname = dto.Hostname,
                LocalPort = dto.LocalPort,
                SecretRef = dto.SecretRef,
                IsActive = dto.IsActive,
                CreatedAt = dto.CreatedAt,
                CreatedBy = dto.CreatedBy
            },
            Credential = new TunnelCredential
            {
                ClientId = dto.Credential.ClientId,
                ClientSecret = dto.Credential.ClientSecret
            }
        }).ToList();
    }

    private async Task RunHealthCheckCycleAsync(CancellationToken ct)
    {
        try
        {
            var statuses = _processService.GetAllStatuses();
            var notRunning = statuses.Where(s => !s.IsRunning).ToList();

            if (notRunning.Any())
            {
                _logger.LogWarning(
                    "Health check: {Count} tunnel(s) not running — restarting...",
                    notRunning.Count);

                foreach (var tunnel in notRunning)
                {
                    if (ct.IsCancellationRequested) break;
                    await _processService.RestartTunnelAsync(tunnel.Hostname);
                }
            }
            else
            {
                _logger.LogInformation(
                    "Health check: All {Count} tunnel(s) running nominally.",
                    statuses.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during health check cycle.");
        }
    }

    // Internal DTOs for deserializing the Config API response
    private class TunnelWithCredential
    {
        public TunnelConfig Config { get; set; } = null!;
        public TunnelCredential Credential { get; set; } = null!;
    }

    private class TunnelWithCredentialDto
    {
        public int Id { get; set; }
        public string Hostname { get; set; } = string.Empty;
        public int LocalPort { get; set; }
        public string SecretRef { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
        public string CreatedBy { get; set; } = string.Empty;
        public TunnelCredential Credential { get; set; } = null!;
    }
}
