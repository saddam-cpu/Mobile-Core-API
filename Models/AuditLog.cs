using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ScreenSharing.Api.Models;

[Table("AuditLogs")]
public class AuditLog
{
    [Key]
    public Guid AuditId { get; set; } = Guid.NewGuid();

    public Guid? AdminId { get; set; }

    public Guid? UserId { get; set; }

    [Required]
    [MaxLength(100)]
    public string Action { get; set; } = string.Empty;

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    [MaxLength(50)]
    public string IPAddress { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string Details { get; set; } = string.Empty;

    // Navigation properties
    [ForeignKey(nameof(AdminId))]
    public AdminUser? Admin { get; set; }

    [ForeignKey(nameof(UserId))]
    public User? User { get; set; }
}
