using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TunnelManager.ConfigApi.Data;
using TunnelManager.ConfigApi.Services;
using TunnelManager.Shared.Models;

namespace TunnelManager.ConfigApi.Endpoints;

public static class TunnelEndpoints
{
    public static void MapTunnelEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/tunnels");

        group.MapGet("/", GetAllTunnels);
        group.MapPost("/", CreateTunnel);
        group.MapPut("/{hostname}", UpdateTunnel);
        group.MapDelete("/{hostname}", DeleteTunnel);
        group.MapGet("/health", GetHealth);
    }

    // GET /tunnels
    private static async Task<IResult> GetAllTunnels(
        TunnelDbContext db,
        IVaultService vaultService,
        IAuditService auditService,
        HttpContext httpContext)
    {
        var ipAddress = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        var tunnels = await db.Tunnels
            .Where(t => t.IsActive)
            .ToListAsync();

        var results = new List<object>();

        foreach (var tunnel in tunnels)
        {
            try
            {
                var credential = await vaultService.ReadSecretAsync(tunnel.SecretRef);

                await auditService.LogAsync(
                    action: AuditActions.CredentialRead,
                    tunnel: tunnel.Hostname,
                    performedBy: "system",
                    ipAddress: ipAddress);

                results.Add(new
                {
                    tunnel.Id,
                    tunnel.Hostname,
                    tunnel.LocalPort,
                    tunnel.SecretRef,
                    tunnel.IsActive,
                    tunnel.CreatedAt,
                    tunnel.CreatedBy,
                    Credential = credential
                });
            }
            catch (Exception ex)
            {
                return Results.Problem(
                    detail: "Vault unavailable",
                    statusCode: StatusCodes.Status503ServiceUnavailable,
                    title: "Vault unavailable");
            }
        }

        return Results.Ok(results);
    }

    // POST /tunnels
    private static async Task<IResult> CreateTunnel(
        [FromBody] AddTunnelRequest request,
        TunnelDbContext db,
        IVaultService vaultService,
        IAuditService auditService,
        HttpContext httpContext)
    {
        var ipAddress = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        // Validate port range
        if (request.LocalPort < 14300 || request.LocalPort > 14500)
        {
            return Results.BadRequest(new { message = "LocalPort must be between 14300 and 14500." });
        }

        // Check hostname uniqueness
        var hostnameExists = await db.Tunnels
            .AnyAsync(t => t.Hostname == request.Hostname && t.IsActive);
        if (hostnameExists)
        {
            return Results.Conflict(new { message = $"Hostname '{request.Hostname}' is already in use." });
        }

        // Check port uniqueness
        var portExists = await db.Tunnels
            .AnyAsync(t => t.LocalPort == request.LocalPort && t.IsActive);
        if (portExists)
        {
            return Results.Conflict(new { message = $"Port {request.LocalPort} is already in use." });
        }

        var secretRef = $"tunnels/{request.Hostname}";
        bool vaultWritten = false;

        try
        {
            // Write credentials to Vault first
            await vaultService.WriteSecretAsync(secretRef, request.ClientId, request.ClientSecret);
            vaultWritten = true;

            var tunnel = new TunnelConfig
            {
                Hostname = request.Hostname,
                LocalPort = request.LocalPort,
                SecretRef = secretRef,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = ipAddress
            };

            db.Tunnels.Add(tunnel);
            await db.SaveChangesAsync();

            await auditService.LogAsync(
                action: AuditActions.TunnelAdded,
                tunnel: request.Hostname,
                performedBy: ipAddress,
                ipAddress: ipAddress);

            return Results.Created($"/tunnels/{tunnel.Hostname}", tunnel);
        }
        catch (Exception ex)
        {
            // Rollback Vault write if DB save fails
            if (vaultWritten)
            {
                try
                {
                    await vaultService.DeleteSecretAsync(secretRef);
                }
                catch
                {
                    // Best-effort rollback — log and continue
                }
            }

            return Results.Problem(
                detail: ex.Message,
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Failed to create tunnel.");
        }
    }

    // PUT /tunnels/{hostname}
    private static async Task<IResult> UpdateTunnel(
        string hostname,
        [FromBody] UpdateTunnelRequest request,
        TunnelDbContext db,
        IVaultService vaultService,
        IAuditService auditService,
        HttpContext httpContext)
    {
        var ipAddress = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        var tunnel = await db.Tunnels
            .FirstOrDefaultAsync(t => t.Hostname == hostname && t.IsActive);

        if (tunnel is null)
            return Results.NotFound(new { message = $"Tunnel '{hostname}' not found." });

        try
        {
            // Update ONLY this tunnel's credentials in Vault
            await vaultService.WriteSecretAsync(tunnel.SecretRef, request.ClientId, request.ClientSecret);

            await auditService.LogAsync(
                action: AuditActions.TunnelUpdated,
                tunnel: hostname,
                performedBy: ipAddress,
                ipAddress: ipAddress);

            return Results.Ok(tunnel);
        }
        catch (Exception ex)
        {
            return Results.Problem(
                detail: ex.Message,
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Failed to update tunnel credentials.");
        }
    }

    // DELETE /tunnels/{hostname}
    private static async Task<IResult> DeleteTunnel(
        string hostname,
        TunnelDbContext db,
        IVaultService vaultService,
        IAuditService auditService,
        HttpContext httpContext)
    {
        var ipAddress = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        var tunnel = await db.Tunnels
            .FirstOrDefaultAsync(t => t.Hostname == hostname && t.IsActive);

        if (tunnel is null)
            return Results.NotFound(new { message = $"Tunnel '{hostname}' not found." });

        // Soft delete — preserve audit trail
        tunnel.IsActive = false;
        await db.SaveChangesAsync();

        // Delete credentials from Vault
        try
        {
            await vaultService.DeleteSecretAsync(tunnel.SecretRef);
        }
        catch (Exception ex)
        {
            // Log but do not fail the request — DB record is already soft-deleted
            Console.Error.WriteLine($"Warning: Failed to delete Vault secret for {hostname}: {ex.Message}");
        }

        await auditService.LogAsync(
            action: AuditActions.TunnelDeleted,
            tunnel: hostname,
            performedBy: ipAddress,
            ipAddress: ipAddress);

        return Results.NoContent();
    }

    // GET /tunnels/health — monitoring only, no credentials
    private static async Task<IResult> GetHealth(
        TunnelDbContext db)
    {
        var tunnels = await db.Tunnels
            .Where(t => t.IsActive)
            .Select(t => new TunnelStatus
            {
                Hostname = t.Hostname,
                LocalPort = t.LocalPort,
                IsRunning = t.IsActive,
                RestartCount = 0,
                LastChecked = DateTime.UtcNow
            })
            .ToListAsync();

        return Results.Ok(new
        {
            status = "healthy",
            tunnelCount = tunnels.Count,
            tunnels
        });
    }
}
