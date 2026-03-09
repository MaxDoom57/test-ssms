using System.ComponentModel.DataAnnotations;

namespace TunnelManager.Shared.Models;

public class AddTunnelRequest
{
    [Required]
    [MaxLength(255)]
    [RegularExpression(@"^[a-zA-Z0-9]([a-zA-Z0-9\-]{0,61}[a-zA-Z0-9])?(\.[a-zA-Z]{2,})+$",
        ErrorMessage = "Hostname must be a valid domain name.")]
    public string Hostname { get; set; } = string.Empty;

    [Required]
    [Range(14300, 14500, ErrorMessage = "LocalPort must be between 14300 and 14500.")]
    public int LocalPort { get; set; }

    [Required]
    [MaxLength(255)]
    public string ClientId { get; set; } = string.Empty;

    [Required]
    [MaxLength(1000)]
    public string ClientSecret { get; set; } = string.Empty;
}
