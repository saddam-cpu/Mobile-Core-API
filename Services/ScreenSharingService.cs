using Microsoft.EntityFrameworkCore;
using ScreenSharing.Api.Data;
using ScreenSharing.Api.DTOs;
using ScreenSharing.Api.Models;

namespace ScreenSharing.Api.Services;

public interface IScreenSharingService
{
    Task<SessionDto> StartSessionAsync(Guid userId, StartSessionRequest request, string? ipAddress);
    Task<bool> StopSessionAsync(Guid userId, StopSessionRequest request, string? ipAddress);
    Task<bool> TerminateSessionByAdminAsync(Guid adminId, Guid sessionId, string reason, string? ipAddress);
    Task<SessionStatusResponse?> GetSessionStatusAsync(Guid sessionId);
    Task<List<SessionDto>> GetUserSessionsAsync(Guid userId, int limit = 20);
    Task<List<ActiveSessionDetailDto>> GetActiveSessionsAsync();
    Task<AdminDashboardStatsDto> GetDashboardStatsAsync();
    Task UpdateConnectionStateAsync(Guid sessionId, string state);
}

public class ScreenSharingService : IScreenSharingService
{
    private readonly AppDbContext _context;
    private readonly IAuditService _auditService;
    private readonly ILogger<ScreenSharingService> _logger;

    public ScreenSharingService(
        AppDbContext context,
        IAuditService auditService,
        ILogger<ScreenSharingService> logger)
    {
        _context = context;
        _auditService = auditService;
        _logger = logger;
    }

    public async Task<SessionDto> StartSessionAsync(Guid userId, StartSessionRequest request, string? ipAddress)
    {
        // 1. Verify device exists or auto-register if provided
        var device = await _context.Devices.FirstOrDefaultAsync(d => d.DeviceId == request.DeviceId);
        if (device == null)
        {
            device = new Device
            {
                DeviceId = request.DeviceId,
                UserId = userId,
                DeviceName = "Android Device",
                AppVersion = request.AppVersion,
                DeviceStatus = DeviceStatuses.Sharing,
                LastSeenAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            };
            _context.Devices.Add(device);
        }
        else
        {
            device.UserId = userId; // Associate with current authenticated user
            device.DeviceStatus = DeviceStatuses.Sharing;
            device.LastSeenAt = DateTime.UtcNow;
            device.AppVersion = request.AppVersion;
        }

        // 2. Mark any previously active sessions on this device as STOPPED
        var staleSessions = await _context.ScreenSharingSessions
            .Where(s => s.DeviceId == request.DeviceId && s.Status == SessionStatuses.Active)
            .ToListAsync();

        foreach (var stale in staleSessions)
        {
            stale.Status = SessionStatuses.Stopped;
            stale.EndedAt = DateTime.UtcNow;
            stale.ConnectionState = PeerConnectionStates.Closed;
        }

        // 3. Create new session
        var session = new ScreenSharingSession
        {
            SessionId = Guid.NewGuid(),
            UserId = userId,
            DeviceId = request.DeviceId,
            StartedAt = DateTime.UtcNow,
            Status = SessionStatuses.Active,
            ConnectionState = PeerConnectionStates.Connecting,
            IPAddress = ipAddress ?? string.Empty,
            AppVersion = request.AppVersion,
            CreatedAt = DateTime.UtcNow
        };

        _context.ScreenSharingSessions.Add(session);
        await _context.SaveChangesAsync();

        var user = await _context.Users.FindAsync(userId);

        await _auditService.LogAsync(
            action: "START_SCREEN_SHARE",
            details: $"User {user?.Email} started screen sharing session {session.SessionId} on device {device.DeviceId}",
            userId: userId,
            ipAddress: ipAddress
        );

        return new SessionDto
        {
            SessionId = session.SessionId,
            UserId = userId,
            UserName = user?.Name ?? string.Empty,
            UserEmail = user?.Email ?? string.Empty,
            DeviceId = device.DeviceId,
            DeviceName = device.DeviceName,
            DeviceModel = device.DeviceModel,
            AndroidVersion = device.AndroidVersion,
            StartedAt = session.StartedAt,
            Status = session.Status,
            ConnectionState = session.ConnectionState
        };
    }

    public async Task<bool> StopSessionAsync(Guid userId, StopSessionRequest request, string? ipAddress)
    {
        var session = await _context.ScreenSharingSessions
            .Include(s => s.Device)
            .Include(s => s.User)
            .FirstOrDefaultAsync(s => s.SessionId == request.SessionId && s.UserId == userId);

        if (session == null)
            return false;

        session.Status = SessionStatuses.Stopped;
        session.EndedAt = DateTime.UtcNow;
        session.ConnectionState = PeerConnectionStates.Closed;

        if (session.Device != null)
        {
            session.Device.DeviceStatus = DeviceStatuses.Online;
            session.Device.LastSeenAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync();

        await _auditService.LogAsync(
            action: "STOP_SCREEN_SHARE",
            details: $"User {session.User?.Email} stopped screen sharing session {session.SessionId}. Reason: {request.Reason}",
            userId: userId,
            ipAddress: ipAddress
        );

        return true;
    }

    public async Task<bool> TerminateSessionByAdminAsync(Guid adminId, Guid sessionId, string reason, string? ipAddress)
    {
        var session = await _context.ScreenSharingSessions
            .Include(s => s.Device)
            .Include(s => s.User)
            .FirstOrDefaultAsync(s => s.SessionId == sessionId);

        if (session == null)
            return false;

        session.Status = SessionStatuses.TerminatedByAdmin;
        session.EndedAt = DateTime.UtcNow;
        session.ConnectionState = PeerConnectionStates.Closed;

        if (session.Device != null)
        {
            session.Device.DeviceStatus = DeviceStatuses.Online;
            session.Device.LastSeenAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync();

        var admin = await _context.AdminUsers.FindAsync(adminId);

        await _auditService.LogAsync(
            action: "ADMIN_TERMINATE_SESSION",
            details: $"Admin {admin?.Email} terminated screen sharing session {sessionId} for user {session.User?.Email}. Reason: {reason}",
            userId: session.UserId,
            adminId: adminId,
            ipAddress: ipAddress
        );

        return true;
    }

    public async Task<SessionStatusResponse?> GetSessionStatusAsync(Guid sessionId)
    {
        var session = await _context.ScreenSharingSessions.FindAsync(sessionId);
        if (session == null)
            return null;

        var isActive = session.Status == SessionStatuses.Active;
        var duration = session.EndedAt.HasValue
            ? (session.EndedAt.Value - session.StartedAt).TotalSeconds
            : (DateTime.UtcNow - session.StartedAt).TotalSeconds;

        return new SessionStatusResponse
        {
            SessionId = session.SessionId,
            Status = session.Status,
            ConnectionState = session.ConnectionState,
            IsActive = isActive,
            StartedAt = session.StartedAt,
            DurationSeconds = duration
        };
    }

    public async Task<List<SessionDto>> GetUserSessionsAsync(Guid userId, int limit = 20)
    {
        return await _context.ScreenSharingSessions
            .Where(s => s.UserId == userId)
            .OrderByDescending(s => s.StartedAt)
            .Take(limit)
            .Select(s => new SessionDto
            {
                SessionId = s.SessionId,
                UserId = s.UserId,
                UserName = s.User != null ? s.User.Name : string.Empty,
                UserEmail = s.User != null ? s.User.Email : string.Empty,
                DeviceId = s.DeviceId,
                DeviceName = s.Device != null ? s.Device.DeviceName : string.Empty,
                DeviceModel = s.Device != null ? s.Device.DeviceModel : string.Empty,
                AndroidVersion = s.Device != null ? s.Device.AndroidVersion : string.Empty,
                StartedAt = s.StartedAt,
                EndedAt = s.EndedAt,
                Status = s.Status,
                ConnectionState = s.ConnectionState
            })
            .ToListAsync();
    }

    public async Task<List<ActiveSessionDetailDto>> GetActiveSessionsAsync()
    {
        return await _context.ScreenSharingSessions
            .Where(s => s.Status == SessionStatuses.Active && s.ConnectionState != PeerConnectionStates.Closed)
            .OrderByDescending(s => s.StartedAt)
            .Select(s => new ActiveSessionDetailDto
            {
                SessionId = s.SessionId,
                UserId = s.UserId,
                UserName = s.User != null ? s.User.Name : "Unknown",
                UserEmail = s.User != null ? s.User.Email : string.Empty,
                UserMobile = s.User != null ? s.User.Mobile : string.Empty,
                DeviceId = s.DeviceId,
                DeviceName = s.Device != null ? s.Device.DeviceName : "Device",
                DeviceModel = s.Device != null ? s.Device.DeviceModel : "Generic Android",
                AndroidVersion = s.Device != null ? s.Device.AndroidVersion : "14",
                AppVersion = s.AppVersion,
                StartedAt = s.StartedAt,
                Status = s.Status,
                ConnectionState = s.ConnectionState,
                IPAddress = s.IPAddress
            })
            .ToListAsync();
    }

    public async Task<AdminDashboardStatsDto> GetDashboardStatsAsync()
    {
        var totalUsers = await _context.Users.CountAsync();
        var onlineUsers = await _context.Devices.CountAsync(d => d.DeviceStatus == DeviceStatuses.Online);
        var sharingUsers = await _context.Devices.CountAsync(d => d.DeviceStatus == DeviceStatuses.Sharing);
        var offlineUsers = await _context.Devices.CountAsync(d => d.DeviceStatus == DeviceStatuses.Offline);
        var activeSessions = await _context.ScreenSharingSessions.CountAsync(s => s.Status == SessionStatuses.Active);

        return new AdminDashboardStatsDto
        {
            TotalUsers = totalUsers,
            OnlineUsers = onlineUsers,
            SharingUsers = sharingUsers,
            OfflineUsers = offlineUsers,
            ActiveSessionsCount = activeSessions
        };
    }

    public async Task UpdateConnectionStateAsync(Guid sessionId, string state)
    {
        var session = await _context.ScreenSharingSessions.FindAsync(sessionId);
        if (session != null)
        {
            session.ConnectionState = state;
            await _context.SaveChangesAsync();
        }
    }
}
