using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ScreenSharing.Api.Models;

[Table("ScreenSharingSessions")]
public class ScreenSharingSession
{
    [Key]
    public Guid SessionId { get; set; } = Guid.NewGuid();

    [Required]
    public Guid UserId { get; set; }

    [Required]
    [MaxLength(100)]
    public string DeviceId { get; set; } = string.Empty;

    public DateTime StartedAt { get; set; } = DateTime.UtcNow;

    public DateTime? EndedAt { get; set; }

    [Required]
    [MaxLength(30)]
    public string Status { get; set; } = SessionStatuses.Active;

    [Required]
    [MaxLength(30)]
    public string ConnectionState { get; set; } = PeerConnectionStates.New;

    [MaxLength(50)]
    public string IPAddress { get; set; } = string.Empty;

    [MaxLength(20)]
    public string AppVersion { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    [ForeignKey(nameof(UserId))]
    public User? User { get; set; }

    [ForeignKey(nameof(DeviceId))]
    public Device? Device { get; set; }
}
