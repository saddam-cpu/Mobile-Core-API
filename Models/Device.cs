using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ScreenSharing.Api.Models;

[Table("Devices")]
public class Device
{
    [Key]
    [MaxLength(100)]
    public string DeviceId { get; set; } = string.Empty;

    [Required]
    public Guid UserId { get; set; }

    [Required]
    [MaxLength(100)]
    public string DeviceName { get; set; } = string.Empty;

    [MaxLength(20)]
    public string AndroidVersion { get; set; } = string.Empty;

    [MaxLength(20)]
    public string AppVersion { get; set; } = string.Empty;

    [MaxLength(100)]
    public string DeviceModel { get; set; } = string.Empty;

    [Required]
    [MaxLength(20)]
    public string DeviceStatus { get; set; } = DeviceStatuses.Offline;

    public DateTime? LastSeenAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    [ForeignKey(nameof(UserId))]
    public User? User { get; set; }

    public ICollection<ScreenSharingSession> Sessions { get; set; } = new List<ScreenSharingSession>();
}
