using System.Security.Cryptography;
using System.Text;
using TunnelManager.ConfigApi.Services;

namespace TunnelManager.ConfigApi.Middleware;

public class InternalKeyMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<InternalKeyMiddleware> _logger;
    private readonly byte[] _expectedKeyBytes;

    public InternalKeyMiddleware(
        RequestDelegate next,
        ILogger<InternalKeyMiddleware> logger,
        IConfiguration configuration)
    {
        _next = next;
        _logger = logger;

        var key = configuration["INTERNAL_CONFIG_API_KEY"]
            ?? throw new InvalidOperationException("INTERNAL_CONFIG_API_KEY environment variable is not set.");

        _expectedKeyBytes = Encoding.UTF8.GetBytes(key);
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments("/health"))
        {
            await _next(context);
            return;
        }

        var ipAddress = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        if (!context.Request.Headers.TryGetValue("x-internal-key", out var receivedKeyValues) ||
            receivedKeyValues.Count == 0 ||
            string.IsNullOrWhiteSpace(receivedKeyValues[0]))
        {
            await HandleUnauthorizedAsync(context, ipAddress, "missing header");
            return;
        }

        var receivedKey = receivedKeyValues[0]!;
        var receivedKeyBytes = Encoding.UTF8.GetBytes(receivedKey);

        // Constant-time comparison to prevent timing attacks
        bool keysMatch = receivedKeyBytes.Length == _expectedKeyBytes.Length &&
                         CryptographicOperations.FixedTimeEquals(receivedKeyBytes, _expectedKeyBytes);

        if (!keysMatch)
        {
            await HandleUnauthorizedAsync(context, ipAddress, "invalid key");
            return;
        }

        await _next(context);
    }

    private async Task HandleUnauthorizedAsync(HttpContext context, string ipAddress, string reason)
    {
        _logger.LogWarning(
            "Unauthorized access attempt from IP {IpAddress}: {Reason}",
            ipAddress, reason);

        // Log to audit service (resolve from DI in the request scope)
        try
        {
            var auditService = context.RequestServices.GetService<IAuditService>();
            if (auditService != null)
            {
                await auditService.LogAsync(
                    action: AuditActions.UnauthorizedAccess,
                    tunnel: "N/A",
                    performedBy: "anonymous",
                    ipAddress: ipAddress);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to log unauthorized access attempt to audit service.");
        }

        // Generic message — never reveal the reason to the caller
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new { message = "Access denied." });
    }
}
