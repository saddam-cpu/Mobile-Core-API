using System.ComponentModel.DataAnnotations;

namespace ScreenSharing.Api.DTOs;

public class AdminDashboardStatsDto
{
    public int TotalUsers { get; set; }
    public int OnlineUsers { get; set; }
    public int SharingUsers { get; set; }
    public int OfflineUsers { get; set; }
    public int ActiveSessionsCount { get; set; }
}

public class ActiveSessionDetailDto
{
    public Guid SessionId { get; set; }
    public Guid UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string UserEmail { get; set; } = string.Empty;
    public string UserMobile { get; set; } = string.Empty;
    public string DeviceId { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public string DeviceModel { get; set; } = string.Empty;
    public string AndroidVersion { get; set; } = string.Empty;
    public string AppVersion { get; set; } = string.Empty;
    public DateTime StartedAt { get; set; }
    public string Status { get; set; } = string.Empty;
    public string ConnectionState { get; set; } = string.Empty;
    public string IPAddress { get; set; } = string.Empty;
    public double DurationSeconds => (DateTime.UtcNow - StartedAt).TotalSeconds;
}

public class UserDetailDto
{
    public Guid UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Mobile { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public List<DeviceDto> Devices { get; set; } = new();
    public int TotalSessionsCount { get; set; }
}

public class AuditLogDto
{
    public Guid AuditId { get; set; }
    public string? AdminEmail { get; set; }
    public string? UserEmail { get; set; }
    public string Action { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public string IPAddress { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
}

public class TerminateSessionRequest
{
    [Required]
    public string Reason { get; set; } = "Terminated by administrator";
}
