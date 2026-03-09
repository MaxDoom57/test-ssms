using System.Collections.Concurrent;
using System.Diagnostics;
using TunnelManager.Shared.Models;

namespace TunnelManager.Worker.Services;

public class TunnelProcessInfo
{
    public required Process Process { get; set; }
    public required TunnelConfig Config { get; set; }
    public required TunnelCredential Credential { get; set; }
    public int RestartCount { get; set; }
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
}

public class TunnelProcessService
{
    private readonly ConcurrentDictionary<string, TunnelProcessInfo> _processes = new();
    private readonly ILogger<TunnelProcessService> _logger;

    public TunnelProcessService(ILogger<TunnelProcessService> logger)
    {
        _logger = logger;
    }

    public async Task<bool> SpawnTunnelAsync(TunnelConfig config, TunnelCredential credential)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "cloudflared",
                Arguments = $"access tcp " +
                            $"--hostname {config.Hostname} " +
                            $"--url localhost:{config.LocalPort} " +
                            $"--service-token-id {credential.ClientId} " +
                            $"--service-token-secret {credential.ClientSecret}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

            process.OutputDataReceived += (_, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                    _logger.LogInformation("[cloudflared:{Hostname}] {Output}", config.Hostname, e.Data);
            };

            process.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                    _logger.LogWarning("[cloudflared:{Hostname}] STDERR: {Output}", config.Hostname, e.Data);
            };

            process.Exited += (_, _) =>
            {
                _logger.LogWarning("[cloudflared:{Hostname}] Process exited with code {ExitCode}",
                    config.Hostname, process.ExitCode);
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            var info = new TunnelProcessInfo
            {
                Process = process,
                Config = config,
                Credential = credential,
                RestartCount = 0,
                StartedAt = DateTime.UtcNow
            };

            _processes[config.Hostname] = info;

            _logger.LogInformation("Tunnel process spawned for {Hostname} on port {Port}",
                config.Hostname, config.LocalPort);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to spawn tunnel process for {Hostname}", config.Hostname);
            return false;
        }
    }

    public async Task StopTunnelAsync(string hostname)
    {
        if (!_processes.TryGetValue(hostname, out var info))
        {
            _logger.LogWarning("Attempted to stop non-existent tunnel: {Hostname}", hostname);
            return;
        }

        try
        {
            if (!info.Process.HasExited)
            {
                info.Process.Kill(entireProcessTree: true);

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                try
                {
                    await info.Process.WaitForExitAsync(cts.Token);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogWarning("Tunnel {Hostname} did not exit within 5 seconds after kill.", hostname);
                }
            }

            info.Process.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping tunnel process for {Hostname}", hostname);
        }
        finally
        {
            _processes.TryRemove(hostname, out _);
            _logger.LogInformation("Tunnel {Hostname} stopped and removed.", hostname);
        }
    }

    public async Task RestartTunnelAsync(string hostname)
    {
        if (!_processes.TryGetValue(hostname, out var info))
        {
            _logger.LogWarning("Cannot restart — tunnel not found: {Hostname}", hostname);
            return;
        }

        var config = info.Config;
        var credential = info.Credential;
        var restartCount = info.RestartCount + 1;

        _logger.LogInformation("Restarting tunnel {Hostname} (restart #{Count})", hostname, restartCount);

        await StopTunnelAsync(hostname);
        await Task.Delay(TimeSpan.FromSeconds(3));

        var success = await SpawnTunnelAsync(config, credential);

        if (success && _processes.TryGetValue(hostname, out var newInfo))
        {
            newInfo.RestartCount = restartCount;
        }
    }

    public IReadOnlyList<TunnelStatus> GetAllStatuses()
    {
        return _processes.Values.Select(info => new TunnelStatus
        {
            Hostname = info.Config.Hostname,
            LocalPort = info.Config.LocalPort,
            IsRunning = !info.Process.HasExited,
            RestartCount = info.RestartCount,
            LastChecked = DateTime.UtcNow
        }).ToList();
    }

    public IReadOnlyDictionary<string, TunnelProcessInfo> GetAllProcesses() => _processes;
}
