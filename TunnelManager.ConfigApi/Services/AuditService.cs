using TunnelManager.ConfigApi.Data;
using TunnelManager.Shared.Models;

namespace TunnelManager.ConfigApi.Services;

public class AuditService : IAuditService
{
    private readonly TunnelDbContext _db;
    private readonly ILogger<AuditService> _logger;

    public AuditService(TunnelDbContext db, ILogger<AuditService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task LogAsync(string action, string tunnel, string performedBy, string ipAddress)
    {
        try
        {
            var auditLog = new AuditLog
            {
                Action = action,
                Tunnel = tunnel,
                PerformedBy = performedBy,
                IpAddress = ipAddress,
                Timestamp = DateTime.UtcNow
            };

            _db.AuditLogs.Add(auditLog);
            await _db.SaveChangesAsync();

            _logger.LogInformation(
                "Audit log recorded: Action={Action}, Tunnel={Tunnel}, By={PerformedBy}, IP={IpAddress}",
                action, tunnel, performedBy, ipAddress);
        }
        catch (Exception ex)
        {
            // Never throw — audit logging must not break the request pipeline
            _logger.LogError(
                ex,
                "Failed to write audit log. Action={Action}, Tunnel={Tunnel}, By={PerformedBy}, IP={IpAddress}",
                action, tunnel, performedBy, ipAddress);
        }
    }
}
