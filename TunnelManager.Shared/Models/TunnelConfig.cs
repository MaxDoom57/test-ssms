using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TunnelManager.Shared.Models;

[Table("tunnels")]
public class TunnelConfig
{
    [Key]
    [Column("id")]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    [Required]
    [MaxLength(255)]
    [Column("hostname")]
    public string Hostname { get; set; } = string.Empty;

    [Range(1, 65535)]
    [Column("local_port")]
    public int LocalPort { get; set; }

    [Required]
    [MaxLength(500)]
    [Column("secret_ref")]
    public string SecretRef { get; set; } = string.Empty;

    [Column("is_active")]
    public bool IsActive { get; set; } = true;

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [MaxLength(255)]
    [Column("created_by")]
    public string CreatedBy { get; set; } = string.Empty;
}
