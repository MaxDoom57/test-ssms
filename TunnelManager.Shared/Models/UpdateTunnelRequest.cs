using System.ComponentModel.DataAnnotations;

namespace TunnelManager.Shared.Models;

public class UpdateTunnelRequest
{
    [Required]
    [MaxLength(255)]
    public string ClientId { get; set; } = string.Empty;

    [Required]
    [MaxLength(1000)]
    public string ClientSecret { get; set; } = string.Empty;
}
