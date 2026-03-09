using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TunnelManager.ConfigApi.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "audit_logs",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    action = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    tunnel = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    performed_by = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    ip_address = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    timestamp = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_logs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tunnels",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    hostname = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    local_port = table.Column<int>(type: "int", nullable: false),
                    secret_ref = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    created_by = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tunnels", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_timestamp",
                table: "audit_logs",
                column: "timestamp");

            migrationBuilder.CreateIndex(
                name: "ix_tunnels_hostname",
                table: "tunnels",
                column: "hostname",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tunnels_local_port",
                table: "tunnels",
                column: "local_port",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_logs");

            migrationBuilder.DropTable(
                name: "tunnels");
        }
    }
}
