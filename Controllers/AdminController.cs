using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using ScreenSharing.Api.Authentication;
using ScreenSharing.Api.Data;
using ScreenSharing.Api.DTOs;
using ScreenSharing.Api.Hubs;
using ScreenSharing.Api.Models;
using ScreenSharing.Api.Services;

namespace ScreenSharing.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AdminController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly IScreenSharingService _screenSharingService;
    private readonly IAuditService _auditService;
    private readonly IHubContext<SignalingHub> _hubContext;
    private readonly ILogger<AdminController> _logger;

    public AdminController(
        AppDbContext context,
        IPasswordHasher passwordHasher,
        IJwtTokenService jwtTokenService,
        IScreenSharingService screenSharingService,
        IAuditService auditService,
        IHubContext<SignalingHub> hubContext,
        ILogger<AdminController> logger)
    {
        _context = context;
        _passwordHasher = passwordHasher;
        _jwtTokenService = jwtTokenService;
        _screenSharingService = screenSharingService;
        _auditService = auditService;
        _hubContext = hubContext;
        _logger = logger;
    }

    private Guid GetCurrentAdminId()
    {
        var idStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(idStr, out var id) ? id : Guid.Empty;
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthResponse>> Login([FromBody] AdminLoginRequest request)
    {
        var email = request.Email.Trim().ToLowerInvariant();

        var admin = await _context.AdminUsers
            .FirstOrDefaultAsync(a => a.Email.ToLower() == email);

        if (admin == null || !_passwordHasher.VerifyPassword(request.Password, admin.PasswordHash))
        {
            return Unauthorized(new { message = "Invalid admin credentials." });
        }

        if (admin.Status != "Active")
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Admin account is disabled." });
        }

        var (accessToken, expiresAt) = _jwtTokenService.GenerateAccessToken(admin.AdminId, admin.Name, admin.Email, admin.Role);
        var refreshTokenValue = _jwtTokenService.GenerateRefreshToken();

        var refreshToken = new RefreshToken
        {
            Token = refreshTokenValue,
            AdminId = admin.AdminId,
            ExpiresAt = DateTime.UtcNow.AddDays(365),
            CreatedAt = DateTime.UtcNow
        };

        _context.RefreshTokens.Add(refreshToken);
        await _context.SaveChangesAsync();

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        await _auditService.LogAsync("ADMIN_LOGIN", $"Admin logged in: {admin.Email}", adminId: admin.AdminId, ipAddress: ip);

        return Ok(new AuthResponse
        {
            AccessToken = accessToken,
            RefreshToken = refreshTokenValue,
            ExpiresAt = expiresAt,
            UserId = admin.AdminId,
            Name = admin.Name,
            Email = admin.Email,
            Role = admin.Role
        });
    }

    [HttpGet("dashboard")]
    [Authorize(Roles = UserRoles.Admin)]
    public async Task<ActionResult<AdminDashboardStatsDto>> GetDashboardStats()
    {
        var stats = await _screenSharingService.GetDashboardStatsAsync();
        return Ok(stats);
    }

    [HttpGet("active-sessions")]
    [Authorize(Roles = UserRoles.Admin)]
    public async Task<ActionResult<List<ActiveSessionDetailDto>>> GetActiveSessions()
    {
        var sessions = await _screenSharingService.GetActiveSessionsAsync();
        return Ok(sessions);
    }

    [HttpGet("session/{sessionId}")]
    [Authorize(Roles = UserRoles.Admin)]
    public async Task<ActionResult<ActiveSessionDetailDto>> GetSessionDetails(Guid sessionId)
    {
        var session = await _context.ScreenSharingSessions
            .Include(s => s.User)
            .Include(s => s.Device)
            .FirstOrDefaultAsync(s => s.SessionId == sessionId);

        if (session == null)
            return NotFound(new { message = "Session not found" });

        var adminId = GetCurrentAdminId();
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        await _auditService.LogAsync("ADMIN_VIEW_STREAM", $"Admin viewed session details for session {sessionId}", adminId: adminId, userId: session.UserId, ipAddress: ip);

        return Ok(new ActiveSessionDetailDto
        {
            SessionId = session.SessionId,
            UserId = session.UserId,
            UserName = session.User?.Name ?? "Unknown",
            UserEmail = session.User?.Email ?? string.Empty,
            UserMobile = session.User?.Mobile ?? string.Empty,
            DeviceId = session.DeviceId,
            DeviceName = session.Device?.DeviceName ?? "Device",
            DeviceModel = session.Device?.DeviceModel ?? "Generic Android",
            AndroidVersion = session.Device?.AndroidVersion ?? "14",
            AppVersion = session.AppVersion,
            StartedAt = session.StartedAt,
            Status = session.Status,
            ConnectionState = session.ConnectionState,
            IPAddress = session.IPAddress
        });
    }

    [HttpPost("session/{sessionId}/terminate")]
    [Authorize(Roles = UserRoles.Admin)]
    public async Task<IActionResult> TerminateSession(Guid sessionId, [FromBody] TerminateSessionRequest request)
    {
        var adminId = GetCurrentAdminId();
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();

        var success = await _screenSharingService.TerminateSessionByAdminAsync(adminId, sessionId, request.Reason, ip);
        if (!success)
            return NotFound(new { message = "Session not found" });

        // Broadcast to SignalR group to stop Android client and update admin dashboard
        var groupName = $"session-{sessionId}";
        await _hubContext.Clients.Group(groupName).SendAsync("ForceStopSession", sessionId.ToString(), request.Reason);
        await _hubContext.Clients.Group("Admins").SendAsync("ScreenSharingStopped", sessionId.ToString());

        return Ok(new { message = "Session terminated successfully", sessionId });
    }

    [HttpPost("device/{deviceId}/view-screen")]
    [Authorize(Roles = UserRoles.Admin)]
    public async Task<ActionResult<ActiveSessionDetailDto>> ViewDeviceScreen(string deviceId)
    {
        var adminId = GetCurrentAdminId();
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();

        var device = await _context.Devices
            .Include(d => d.User)
            .FirstOrDefaultAsync(d => d.DeviceId == deviceId);

        if (device == null)
            return NotFound(new { message = "Device not found" });

        // Clean up any old active sessions on this device
        var staleSessions = await _context.ScreenSharingSessions
            .Where(s => s.DeviceId == deviceId && s.Status == "ACTIVE")
            .ToListAsync();

        foreach (var stale in staleSessions)
        {
            stale.Status = "STOPPED";
            stale.EndedAt = DateTime.UtcNow;
            stale.ConnectionState = "CLOSED";
        }

        var newSession = new ScreenSharingSession
        {
            SessionId = Guid.NewGuid(),
            UserId = device.UserId,
            DeviceId = deviceId,
            AppVersion = device.AppVersion,
            StartedAt = DateTime.UtcNow,
            Status = "ACTIVE",
            ConnectionState = "Connecting",
            IPAddress = ip
        };

        _context.ScreenSharingSessions.Add(newSession);
        device.DeviceStatus = "SHARING";
        device.LastSeenAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        await _auditService.LogAsync("ADMIN_VIEW_STREAM", $"Admin initiated direct screen view for device {deviceId} (Session {newSession.SessionId})", adminId: adminId, userId: device.UserId, ipAddress: ip);

        // Send SignalR RequestScreenShare to device and user
        await _hubContext.Clients.Group($"device-{deviceId}").SendAsync("RequestScreenShare", newSession.SessionId.ToString());
        await _hubContext.Clients.Group($"user-{device.UserId}").SendAsync("RequestScreenShare", newSession.SessionId.ToString());
        await _hubContext.Clients.All.SendAsync("RequestScreenShare", newSession.SessionId.ToString());

        return Ok(new ActiveSessionDetailDto
        {
            SessionId = newSession.SessionId,
            UserId = newSession.UserId,
            UserName = device.User?.Name ?? "User",
            UserEmail = device.User?.Email ?? string.Empty,
            UserMobile = device.User?.Mobile ?? string.Empty,
            DeviceId = newSession.DeviceId,
            DeviceName = device.DeviceName,
            DeviceModel = device.DeviceModel,
            AndroidVersion = device.AndroidVersion,
            AppVersion = newSession.AppVersion,
            StartedAt = newSession.StartedAt,
            Status = newSession.Status,
            ConnectionState = newSession.ConnectionState,
            IPAddress = newSession.IPAddress
        });
    }

    [HttpGet("users")]
    [Authorize(Roles = UserRoles.Admin)]
    public async Task<ActionResult<List<UserDetailDto>>> GetUsers()
    {
        var users = await _context.Users
            .Include(u => u.Devices)
            .Include(u => u.Sessions)
            .OrderByDescending(u => u.CreatedAt)
            .Select(u => new UserDetailDto
            {
                UserId = u.UserId,
                Name = u.Name,
                Email = u.Email,
                Mobile = u.Mobile,
                Status = u.Status,
                CreatedAt = u.CreatedAt,
                TotalSessionsCount = u.Sessions.Count,
                Devices = u.Devices.Select(d => new DeviceDto
                {
                    DeviceId = d.DeviceId,
                    DeviceName = d.DeviceName,
                    AndroidVersion = d.AndroidVersion,
                    AppVersion = d.AppVersion,
                    DeviceModel = d.DeviceModel,
                    DeviceStatus = d.DeviceStatus,
                    LastSeenAt = d.LastSeenAt
                }).ToList()
            })
            .ToListAsync();

        return Ok(users);
    }

    [HttpGet("audit-logs")]
    [Authorize(Roles = UserRoles.Admin)]
    public async Task<ActionResult<List<AuditLogDto>>> GetAuditLogs([FromQuery] int limit = 50)
    {
        var logs = await _context.AuditLogs
            .Include(a => a.Admin)
            .Include(a => a.User)
            .OrderByDescending(a => a.Timestamp)
            .Take(limit)
            .Select(a => new AuditLogDto
            {
                AuditId = a.AuditId,
                AdminEmail = a.Admin != null ? a.Admin.Email : null,
                UserEmail = a.User != null ? a.User.Email : null,
                Action = a.Action,
                Timestamp = a.Timestamp,
                IPAddress = a.IPAddress,
                Details = a.Details
            })
            .ToListAsync();

        return Ok(logs);
    }
}
