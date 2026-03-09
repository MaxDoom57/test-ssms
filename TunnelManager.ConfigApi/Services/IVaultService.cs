using TunnelManager.Shared.Models;

namespace TunnelManager.ConfigApi.Services;

public interface IVaultService
{
    Task WriteSecretAsync(string path, string clientId, string clientSecret);
    Task<TunnelCredential> ReadSecretAsync(string path);
    Task DeleteSecretAsync(string path);
}
