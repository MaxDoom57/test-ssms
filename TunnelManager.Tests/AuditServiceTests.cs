using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using TunnelManager.ConfigApi.Data;
using TunnelManager.ConfigApi.Services;
using Xunit;

namespace TunnelManager.Tests;

public class AuditServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly TunnelDbContext _db;

    public AuditServiceTests()
    {
        // Use SQLite in-memory (keeps connection open so schema persists for the test)
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<TunnelDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new TunnelDbContext(options);
        _db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task LogAsync_WritesCorrectActionToDatabase()
    {
        // Arrange
        var logger = new Mock<ILogger<AuditService>>();
        var service = new AuditService(_db, logger.Object);

        // Act
        await service.LogAsync(
            action: AuditActions.TunnelAdded,
            tunnel: "test.example.com",
            performedBy: "operator",
            ipAddress: "127.0.0.1");

        // Assert
        var log = _db.AuditLogs.FirstOrDefault();
        Assert.NotNull(log);
        Assert.Equal(AuditActions.TunnelAdded, log.Action);
        Assert.Equal("test.example.com", log.Tunnel);
        Assert.Equal("operator", log.PerformedBy);
        Assert.Equal("127.0.0.1", log.IpAddress);
    }

    [Fact]
    public async Task LogAsync_DoesNotThrowWhenDatabaseUnavailable()
    {
        // Arrange — use a disposed context to simulate DB unavailability
        var disposedConnection = new SqliteConnection("Data Source=:memory:");
        disposedConnection.Open();
        var opts = new DbContextOptionsBuilder<TunnelDbContext>()
            .UseSqlite(disposedConnection)
            .Options;
        var disposedDb = new TunnelDbContext(opts);
        disposedDb.Dispose(); // Force an error state

        var logger = new Mock<ILogger<AuditService>>();
        var service = new AuditService(disposedDb, logger.Object);

        // Act & Assert — must not throw
        var exception = await Record.ExceptionAsync(() =>
            service.LogAsync("TUNNEL_ADDED", "test.example.com", "op", "1.2.3.4"));

        Assert.Null(exception);
        disposedConnection.Dispose();
    }
}
