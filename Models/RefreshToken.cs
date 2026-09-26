using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ScreenSharing.Api.Models;

[Table("RefreshTokens")]
public class RefreshToken
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    [MaxLength(200)]
    public string Token { get; set; } = string.Empty;

    public Guid? UserId { get; set; }

    public Guid? AdminId { get; set; }

    public DateTime ExpiresAt { get; set; }

    public bool IsRevoked { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public string? ReplacedByToken { get; set; }

    public bool IsActive => !IsRevoked && DateTime.UtcNow < ExpiresAt;

    [ForeignKey(nameof(UserId))]
    public User? User { get; set; }

    [ForeignKey(nameof(AdminId))]
    public AdminUser? Admin { get; set; }
}
