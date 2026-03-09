using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TunnelManager.Shared.Models;

[Table("audit_logs")]
public class AuditLog
{
    [Key]
    [Column("id")]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    [Required]
    [MaxLength(100)]
    [Column("action")]
    public string Action { get; set; } = string.Empty;

    [Required]
    [MaxLength(255)]
    [Column("tunnel")]
    public string Tunnel { get; set; } = string.Empty;

    [MaxLength(255)]
    [Column("performed_by")]
    public string PerformedBy { get; set; } = string.Empty;

    [MaxLength(50)]
    [Column("ip_address")]
    public string IpAddress { get; set; } = string.Empty;

    [Column("timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
