using System.ComponentModel.DataAnnotations;

namespace ScreenSharing.Api.DTOs;

public class StartSessionRequest
{
    [Required]
    public string DeviceId { get; set; } = string.Empty;

    public string AppVersion { get; set; } = "1.0.0";
}

public class StopSessionRequest
{
    [Required]
    public Guid SessionId { get; set; }

    public string Reason { get; set; } = "User stopped session";
}

public class SessionDto
{
    public Guid SessionId { get; set; }
    public Guid UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string UserEmail { get; set; } = string.Empty;
    public string DeviceId { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public string DeviceModel { get; set; } = string.Empty;
    public string AndroidVersion { get; set; } = string.Empty;
    public DateTime StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public string Status { get; set; } = string.Empty;
    public string ConnectionState { get; set; } = string.Empty;
    public double DurationSeconds => EndedAt.HasValue
        ? (EndedAt.Value - StartedAt).TotalSeconds
        : (DateTime.UtcNow - StartedAt).TotalSeconds;
}

public class SessionStatusResponse
{
    public Guid SessionId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string ConnectionState { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTime StartedAt { get; set; }
    public double DurationSeconds { get; set; }
}
