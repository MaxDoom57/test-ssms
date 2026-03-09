using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using TunnelManager.ConfigApi.Services;
using Xunit;

namespace TunnelManager.Tests;

public class VaultServiceTests
{
    /// <summary>
    /// VaultService is tightly coupled to VaultSharp internals, so we test
    /// its error-path behaviour by testing construction-time validation.
    /// </summary>

    private static IConfiguration BuildConfig(string? vaultAddr, string? vaultToken)
    {
        var dict = new Dictionary<string, string?>();
        if (vaultAddr != null) dict["VAULT_ADDR"] = vaultAddr;
        if (vaultToken != null) dict["VAULT_TOKEN"] = vaultToken;

        return new ConfigurationBuilder()
            .AddInMemoryCollection(dict)
            .Build();
    }

    [Fact]
    public void Constructor_ThrowsIfVaultAddrMissing()
    {
        // Arrange — VAULT_ADDR is absent
        var config = BuildConfig(vaultAddr: null, vaultToken: "test-token");
        var logger = new Mock<ILogger<VaultService>>();

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() =>
            new VaultService(config, logger.Object));
    }

    [Fact]
    public void Constructor_ThrowsIfVaultTokenMissing()
    {
        // Arrange — VAULT_TOKEN is absent
        var config = BuildConfig(vaultAddr: "http://vault:8200", vaultToken: null);
        var logger = new Mock<ILogger<VaultService>>();

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() =>
            new VaultService(config, logger.Object));
    }

    [Fact]
    public void Constructor_Succeeds_WhenBothSettingsProvided()
    {
        // Arrange
        var config = BuildConfig("http://vault:8200", "test-token");
        var logger = new Mock<ILogger<VaultService>>();

        // Act & Assert — no exception
        var service = new VaultService(config, logger.Object);
        Assert.NotNull(service);
    }
}
