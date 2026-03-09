using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using TunnelManager.ConfigApi.Data;
using TunnelManager.ConfigApi.Services;
using TunnelManager.Shared.Models;
using Xunit;

namespace TunnelManager.Tests;

public class TunnelEndpointsTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly SqliteConnection _sqliteConnection;

    public TunnelEndpointsTests(WebApplicationFactory<Program> factory)
    {
        // Keep a single SQLite in-memory connection open for the lifetime of the fixture
        _sqliteConnection = new SqliteConnection("Data Source=:memory:");
        _sqliteConnection.Open();

        _factory = factory.WithWebHostBuilder(builder =>
        {
            // All required environment settings
            builder.UseSetting("INTERNAL_CONFIG_API_KEY", "test-key");
            builder.UseSetting("VAULT_ADDR", "http://vault:8200");
            builder.UseSetting("VAULT_TOKEN", "test-token");
            builder.UseSetting("DATABASE_URL", "Data Source=:memory:");

            builder.ConfigureServices(services =>
            {
                // ── Replace Npgsql DbContext with SQLite ──────────────────
                var dbDescriptor = services.SingleOrDefault(d =>
                    d.ServiceType == typeof(DbContextOptions<TunnelDbContext>));
                if (dbDescriptor != null) services.Remove(dbDescriptor);

                services.AddDbContext<TunnelDbContext>(opts =>
                    opts.UseSqlite(_sqliteConnection));

                // ── Replace VaultService with a no-op mock ────────────────
                var vaultDescriptor = services.SingleOrDefault(d =>
                    d.ServiceType == typeof(IVaultService));
                if (vaultDescriptor != null) services.Remove(vaultDescriptor);

                var mockVault = new Mock<IVaultService>();
                mockVault
                    .Setup(v => v.WriteSecretAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                    .Returns(Task.CompletedTask);
                mockVault
                    .Setup(v => v.ReadSecretAsync(It.IsAny<string>()))
                    .ReturnsAsync(new TunnelCredential { ClientId = "id", ClientSecret = "secret" });
                mockVault
                    .Setup(v => v.DeleteSecretAsync(It.IsAny<string>()))
                    .Returns(Task.CompletedTask);

                services.AddScoped<IVaultService>(_ => mockVault.Object);
            });
        });

        // Ensure schema is created
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TunnelDbContext>();
        db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _factory.Dispose();
        _sqliteConnection.Dispose();
    }

    private HttpClient CreateAuthenticatedClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("x-internal-key", "test-key");
        return client;
    }

    // ── POST /tunnels ─────────────────────────────────────────────────────

    [Fact]
    public async Task PostTunnel_Returns400_ForInvalidPort()
    {
        var client = CreateAuthenticatedClient();
        var request = new AddTunnelRequest
        {
            Hostname = "test.example.com",
            LocalPort = 9999, // outside 14300-14500
            ClientId = "client-id",
            ClientSecret = "client-secret"
        };

        var response = await client.PostAsJsonAsync("/tunnels", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PostTunnel_Returns409_IfHostnameAlreadyExists()
    {
        var client = CreateAuthenticatedClient();

        // Seed the database with an existing active tunnel
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TunnelDbContext>();
        db.Tunnels.Add(new TunnelConfig
        {
            Hostname = "conflict.example.com",
            LocalPort = 14310,
            SecretRef = "tunnels/conflict.example.com",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        });
        await db.SaveChangesAsync();

        var request = new AddTunnelRequest
        {
            Hostname = "conflict.example.com", // duplicate
            LocalPort = 14311,
            ClientId = "id",
            ClientSecret = "secret"
        };

        var response = await client.PostAsJsonAsync("/tunnels", request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    // ── DELETE /tunnels/{hostname} ────────────────────────────────────────

    [Fact]
    public async Task DeleteTunnel_Returns404_IfHostnameNotFound()
    {
        var client = CreateAuthenticatedClient();

        var response = await client.DeleteAsync("/tunnels/nonexistent.example.com");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── Auth checks ───────────────────────────────────────────────────────

    [Fact]
    public async Task GetTunnels_Returns403_WithoutInternalKey()
    {
        var client = _factory.CreateClient(); // no auth header

        var response = await client.GetAsync("/tunnels");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
