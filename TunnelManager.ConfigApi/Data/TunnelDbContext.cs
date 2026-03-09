using Microsoft.EntityFrameworkCore;
using TunnelManager.Shared.Models;

namespace TunnelManager.ConfigApi.Data;

public class TunnelDbContext : DbContext
{
    public TunnelDbContext(DbContextOptions<TunnelDbContext> options) : base(options) { }

    public DbSet<TunnelConfig> Tunnels => Set<TunnelConfig>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Tunnels table configuration
        modelBuilder.Entity<TunnelConfig>(entity =>
        {
            entity.ToTable("tunnels");

            // Unique constraint on hostname
            entity.HasIndex(t => t.Hostname)
                  .IsUnique()
                  .HasDatabaseName("ix_tunnels_hostname");

            // Unique constraint on local_port
            entity.HasIndex(t => t.LocalPort)
                  .IsUnique()
                  .HasDatabaseName("ix_tunnels_local_port");

            entity.Property(t => t.CreatedAt)
                  .HasDefaultValueSql("NOW()");

            entity.Property(t => t.IsActive)
                  .HasDefaultValue(true);
        });

        // AuditLogs table configuration
        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.ToTable("audit_logs");

            // Index on timestamp for efficient querying
            entity.HasIndex(a => a.Timestamp)
                  .HasDatabaseName("ix_audit_logs_timestamp");
        });
    }
}
