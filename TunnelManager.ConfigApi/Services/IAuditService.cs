namespace TunnelManager.ConfigApi.Services;

public interface IAuditService
{
    Task LogAsync(string action, string tunnel, string performedBy, string ipAddress);
}

public static class AuditActions
{
    public const string CredentialRead = "CREDENTIAL_READ";
    public const string TunnelAdded = "TUNNEL_ADDED";
    public const string TunnelUpdated = "TUNNEL_UPDATED";
    public const string TunnelDeleted = "TUNNEL_DELETED";
    public const string UnauthorizedAccess = "UNAUTHORIZED_ACCESS";
}
