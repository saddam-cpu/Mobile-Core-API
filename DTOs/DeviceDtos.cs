using System.ComponentModel.DataAnnotations;

namespace ScreenSharing.Api.DTOs;

public class RegisterDeviceRequest
{
    [Required]
    [MaxLength(100)]
    public string DeviceId { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string DeviceName { get; set; } = string.Empty;

    [MaxLength(20)]
    public string AndroidVersion { get; set; } = string.Empty;

    [MaxLength(20)]
    public string AppVersion { get; set; } = string.Empty;

    [MaxLength(100)]
    public string DeviceModel { get; set; } = string.Empty;
}

public class DeviceDto
{
    public string DeviceId { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public string AndroidVersion { get; set; } = string.Empty;
    public string AppVersion { get; set; } = string.Empty;
    public string DeviceModel { get; set; } = string.Empty;
    public string DeviceStatus { get; set; } = string.Empty;
    public DateTime? LastSeenAt { get; set; }
}

public class UpdateDeviceStatusRequest
{
    [Required]
    public string Status { get; set; } = string.Empty; // ONLINE, OFFLINE, SHARING
}
