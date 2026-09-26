using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ScreenSharing.Api.Data;
using ScreenSharing.Api.DTOs;
using ScreenSharing.Api.Models;

namespace ScreenSharing.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class UserController : ControllerBase
{
    private readonly AppDbContext _context;

    public UserController(AppDbContext context)
    {
        _context = context;
    }

    private Guid GetCurrentUserId()
    {
        var idStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(idStr, out var id) ? id : Guid.Empty;
    }

    [HttpGet("profile")]
    public async Task<IActionResult> GetProfile()
    {
        var userId = GetCurrentUserId();
        var user = await _context.Users
            .Include(u => u.Devices)
            .FirstOrDefaultAsync(u => u.UserId == userId);

        if (user == null)
            return NotFound(new { message = "User not found" });

        return Ok(new
        {
            user.UserId,
            user.Name,
            user.Email,
            user.Mobile,
            user.Status,
            user.CreatedAt,
            Devices = user.Devices.Select(d => new DeviceDto
            {
                DeviceId = d.DeviceId,
                DeviceName = d.DeviceName,
                AndroidVersion = d.AndroidVersion,
                AppVersion = d.AppVersion,
                DeviceModel = d.DeviceModel,
                DeviceStatus = d.DeviceStatus,
                LastSeenAt = d.LastSeenAt
            })
        });
    }

    [HttpGet("devices")]
    public async Task<ActionResult<List<DeviceDto>>> GetDevices()
    {
        var userId = GetCurrentUserId();
        var devices = await _context.Devices
            .Where(d => d.UserId == userId)
            .Select(d => new DeviceDto
            {
                DeviceId = d.DeviceId,
                DeviceName = d.DeviceName,
                AndroidVersion = d.AndroidVersion,
                AppVersion = d.AppVersion,
                DeviceModel = d.DeviceModel,
                DeviceStatus = d.DeviceStatus,
                LastSeenAt = d.LastSeenAt
            })
            .ToListAsync();

        return Ok(devices);
    }

    [HttpPost("device/register")]
    public async Task<IActionResult> RegisterDevice([FromBody] RegisterDeviceRequest request)
    {
        var userId = GetCurrentUserId();
        var device = await _context.Devices.FirstOrDefaultAsync(d => d.DeviceId == request.DeviceId);

        if (device == null)
        {
            device = new Device
            {
                DeviceId = request.DeviceId,
                UserId = userId,
                DeviceName = request.DeviceName,
                AndroidVersion = request.AndroidVersion,
                AppVersion = request.AppVersion,
                DeviceModel = request.DeviceModel,
                DeviceStatus = DeviceStatuses.Online,
                LastSeenAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            };
            _context.Devices.Add(device);
        }
        else
        {
            device.UserId = userId;
            device.DeviceName = request.DeviceName;
            device.AndroidVersion = request.AndroidVersion;
            device.AppVersion = request.AppVersion;
            device.DeviceModel = request.DeviceModel;
            device.DeviceStatus = DeviceStatuses.Online;
            device.LastSeenAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync();

        return Ok(new DeviceDto
        {
            DeviceId = device.DeviceId,
            DeviceName = device.DeviceName,
            AndroidVersion = device.AndroidVersion,
            AppVersion = device.AppVersion,
            DeviceModel = device.DeviceModel,
            DeviceStatus = device.DeviceStatus,
            LastSeenAt = device.LastSeenAt
        });
    }

    [HttpPut("device/{deviceId}/status")]
    public async Task<IActionResult> UpdateDeviceStatus(string deviceId, [FromBody] UpdateDeviceStatusRequest request)
    {
        var userId = GetCurrentUserId();
        var device = await _context.Devices.FirstOrDefaultAsync(d => d.DeviceId == deviceId && d.UserId == userId);

        if (device == null)
            return NotFound(new { message = "Device not found" });

        device.DeviceStatus = request.Status;
        device.LastSeenAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return Ok(new { message = "Device status updated", status = device.DeviceStatus });
    }
}
