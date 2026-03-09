using TunnelManager.Shared.Models;
using VaultSharp;
using VaultSharp.V1.AuthMethods.Token;
using VaultSharp.V1.Commons;

namespace TunnelManager.ConfigApi.Services;

public class VaultService : IVaultService
{
    private readonly IVaultClient _vaultClient;
    private readonly ILogger<VaultService> _logger;
    private const string MountPath = "tunnels";

    public VaultService(IConfiguration configuration, ILogger<VaultService> logger)
    {
        _logger = logger;

        var vaultAddr = configuration["VAULT_ADDR"]
            ?? throw new InvalidOperationException("VAULT_ADDR environment variable is not set.");
        var vaultToken = configuration["VAULT_TOKEN"]
            ?? throw new InvalidOperationException("VAULT_TOKEN environment variable is not set.");

        var authMethod = new TokenAuthMethodInfo(vaultToken);
        var vaultClientSettings = new VaultClientSettings(vaultAddr, authMethod);
        _vaultClient = new VaultClient(vaultClientSettings);
    }

    public async Task WriteSecretAsync(string path, string clientId, string clientSecret)
    {
        try
        {
            var secretData = new Dictionary<string, object>
            {
                ["client_id"] = clientId,
                ["client_secret"] = clientSecret
            };

            await _vaultClient.V1.Secrets.KeyValue.V2.WriteSecretAsync(
                path: path,
                data: secretData,
                mountPoint: MountPath);

            _logger.LogInformation("Secret written to Vault path: {MountPath}/{Path}", MountPath, path);
        }
        catch (VaultSharp.Core.VaultApiException ex)
        {
            _logger.LogError("Vault API error writing secret to path {Path}: {Message}", path, ex.Message);
            throw new InvalidOperationException($"Failed to write secret to Vault: {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError("Unexpected error writing secret to Vault path {Path}: {Message}", path, ex.Message);
            throw new InvalidOperationException("An unexpected error occurred while writing to Vault.", ex);
        }
    }

    public async Task<TunnelCredential> ReadSecretAsync(string path)
    {
        try
        {
            Secret<SecretData> secret = await _vaultClient.V1.Secrets.KeyValue.V2.ReadSecretAsync(
                path: path,
                mountPoint: MountPath);

            if (secret?.Data?.Data == null)
                throw new InvalidOperationException($"No secret data found at path: {MountPath}/{path}");

            var data = secret.Data.Data;

            if (!data.TryGetValue("client_id", out var clientIdObj) ||
                !data.TryGetValue("client_secret", out var clientSecretObj))
            {
                throw new InvalidOperationException($"Secret at path {MountPath}/{path} is missing required fields.");
            }

            // NOTE: Never log raw secret values
            _logger.LogInformation("Secret read successfully from Vault path: {MountPath}/{Path}", MountPath, path);

            return new TunnelCredential
            {
                ClientId = clientIdObj?.ToString() ?? string.Empty,
                ClientSecret = clientSecretObj?.ToString() ?? string.Empty
            };
        }
        catch (VaultSharp.Core.VaultApiException ex)
        {
            _logger.LogError("Vault API error reading secret from path {Path}: {Message}", path, ex.Message);
            throw new InvalidOperationException($"Failed to read secret from Vault at path '{path}': {ex.Message}", ex);
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError("Unexpected error reading secret from Vault path {Path}: {Message}", path, ex.Message);
            throw new InvalidOperationException("An unexpected error occurred while reading from Vault.", ex);
        }
    }

    public async Task DeleteSecretAsync(string path)
    {
        try
        {
            await _vaultClient.V1.Secrets.KeyValue.V2.DeleteSecretAsync(
                path: path,
                mountPoint: MountPath);

            _logger.LogInformation("Secret deleted from Vault path: {MountPath}/{Path}", MountPath, path);
        }
        catch (VaultSharp.Core.VaultApiException ex)
        {
            _logger.LogError("Vault API error deleting secret at path {Path}: {Message}", path, ex.Message);
            throw new InvalidOperationException($"Failed to delete secret from Vault: {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError("Unexpected error deleting secret from Vault path {Path}: {Message}", path, ex.Message);
            throw new InvalidOperationException("An unexpected error occurred while deleting from Vault.", ex);
        }
    }
}
