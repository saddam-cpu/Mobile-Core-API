using System.Collections.Concurrent;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using ScreenSharing.Api.Models;
using ScreenSharing.Api.Services;

namespace ScreenSharing.Api.Hubs;

[Authorize]
public class SignalingHub : Hub
{
    private static readonly ConcurrentDictionary<string, string> _cachedOffers = new();
    private static readonly ConcurrentDictionary<string, ConcurrentBag<string>> _cachedCandidates = new();

    private readonly IScreenSharingService _screenSharingService;
    private readonly IAuditService _auditService;
    private readonly ILogger<SignalingHub> _logger;

    public SignalingHub(
        IScreenSharingService screenSharingService,
        IAuditService auditService,
        ILogger<SignalingHub> logger)
    {
        _screenSharingService = screenSharingService;
        _auditService = auditService;
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        var role = Context.User?.FindFirst(ClaimTypes.Role)?.Value;
        var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var email = Context.User?.FindFirst(ClaimTypes.Email)?.Value;

        if (role == UserRoles.Admin)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, "Admins");
            _logger.LogInformation("Admin connected to SignalingHub: {Email} ({ConnectionId})", email, Context.ConnectionId);
        }
        else
        {
            _logger.LogInformation("User connected to SignalingHub: {Email} ({ConnectionId})", email, Context.ConnectionId);
            if (!string.IsNullOrEmpty(userId))
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, $"user-{userId}");
            }
            // Notify admins that user is online
            await Clients.Group("Admins").SendAsync("UserOnline", new
            {
                UserId = userId,
                Email = email,
                ConnectionId = Context.ConnectionId,
                Timestamp = DateTime.UtcNow
            });
        }

        await base.OnConnectedAsync();
    }

    /// <summary>
    /// Android device registers its deviceId with SignalR connection.
    /// </summary>
    public async Task RegisterDevice(string deviceId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"device-{deviceId}");
        _logger.LogInformation("Connection {ConnectionId} registered for device {DeviceId}", Context.ConnectionId, deviceId);
    }

    /// <summary>
    /// Admin requests mobile device to start screen sharing and stream directly.
    /// </summary>
    [Authorize(Roles = UserRoles.Admin)]
    public async Task RequestDeviceScreenShare(string deviceId, string sessionId)
    {
        _logger.LogInformation("Admin requested screen share for device {DeviceId}, session {SessionId}", deviceId, sessionId);
        await Clients.Group($"device-{deviceId}").SendAsync("RequestScreenShare", sessionId);
        await Clients.All.SendAsync("RequestScreenShare", sessionId);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var role = Context.User?.FindFirst(ClaimTypes.Role)?.Value;
        var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var email = Context.User?.FindFirst(ClaimTypes.Email)?.Value;

        if (role != UserRoles.Admin && !string.IsNullOrEmpty(userId))
        {
            _logger.LogInformation("User disconnected from SignalingHub: {Email} ({ConnectionId})", email, Context.ConnectionId);
            await Clients.Group("Admins").SendAsync("UserOffline", new
            {
                UserId = userId,
                Email = email,
                Timestamp = DateTime.UtcNow
            });
        }

        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Join a specific screen sharing session room.
    /// </summary>
    public async Task JoinSession(string sessionId)
    {
        var groupName = $"session-{sessionId}";
        await Groups.AddToGroupAsync(Context.ConnectionId, groupName);

        var role = Context.User?.FindFirst(ClaimTypes.Role)?.Value;
        var email = Context.User?.FindFirst(ClaimTypes.Email)?.Value;

        _logger.LogInformation("Connection {ConnectionId} ({Role}:{Email}) joined session {SessionId}",
            Context.ConnectionId, role, email, sessionId);

        if (role == UserRoles.Admin)
        {
            // If the Android device has already generated an SDP Offer, deliver it directly to this Admin
            if (_cachedOffers.TryGetValue(sessionId, out var cachedOffer))
            {
                _logger.LogInformation("Delivering cached SDP Offer for session {SessionId} to joining Admin {Email}", sessionId, email);
                await Clients.Caller.SendAsync("ReceiveOffer", sessionId, cachedOffer);

                if (_cachedCandidates.TryGetValue(sessionId, out var candidates))
                {
                    foreach (var c in candidates)
                    {
                        await Clients.Caller.SendAsync("ReceiveIceCandidate", sessionId, c);
                    }
                }
            }

            // Inform Android client in this session that an Admin is viewing and request fresh offer/re-send
            await Clients.OthersInGroup(groupName).SendAsync("AdminJoinedSession", new
            {
                SessionId = sessionId,
                AdminEmail = email,
                Timestamp = DateTime.UtcNow
            });
            await Clients.OthersInGroup(groupName).SendAsync("OfferRequested", sessionId);
        }
    }

    /// <summary>
    /// Explicitly request the active SDP offer for a session (used by late-joining web viewers).
    /// </summary>
    public async Task RequestOffer(string sessionId)
    {
        var groupName = $"session-{sessionId}";
        if (_cachedOffers.TryGetValue(sessionId, out var cachedOffer))
        {
            _logger.LogInformation("Delivering cached SDP Offer on RequestOffer for session {SessionId}", sessionId);
            await Clients.Caller.SendAsync("ReceiveOffer", sessionId, cachedOffer);

            if (_cachedCandidates.TryGetValue(sessionId, out var candidates))
            {
                foreach (var c in candidates)
                {
                    await Clients.Caller.SendAsync("ReceiveIceCandidate", sessionId, c);
                }
            }
        }

        // Also ask Android device to re-publish if active
        await Clients.OthersInGroup(groupName).SendAsync("OfferRequested", sessionId);
        await Clients.All.SendAsync("OfferRequested", sessionId);
    }

    /// <summary>
    /// Leave a specific screen sharing session room.
    /// </summary>
    public async Task LeaveSession(string sessionId)
    {
        var groupName = $"session-{sessionId}";
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);

        var role = Context.User?.FindFirst(ClaimTypes.Role)?.Value;
        var email = Context.User?.FindFirst(ClaimTypes.Email)?.Value;

        _logger.LogInformation("Connection {ConnectionId} ({Role}:{Email}) left session {SessionId}",
            Context.ConnectionId, role, email, sessionId);

        if (role == UserRoles.Admin)
        {
            await Clients.OthersInGroup(groupName).SendAsync("AdminLeftSession", new
            {
                SessionId = sessionId,
                AdminEmail = email,
                Timestamp = DateTime.UtcNow
            });
        }
    }

    /// <summary>
    /// Forward WebRTC SDP Offer from Android to Admin viewer and cache it for late joiners.
    /// </summary>
    public async Task SendOffer(string sessionId, string sdp)
    {
        _cachedOffers[sessionId] = sdp;
        var groupName = $"session-{sessionId}";
        _logger.LogInformation("Relaying and caching SDP Offer for session {SessionId}", sessionId);
        await Clients.OthersInGroup(groupName).SendAsync("ReceiveOffer", sessionId, sdp);
    }

    /// <summary>
    /// Forward WebRTC SDP Answer from Admin viewer to Android device.
    /// </summary>
    public async Task SendAnswer(string sessionId, string sdp)
    {
        var groupName = $"session-{sessionId}";
        _logger.LogInformation("Relaying SDP Answer for session {SessionId}", sessionId);
        await Clients.OthersInGroup(groupName).SendAsync("ReceiveAnswer", sessionId, sdp);
    }

    /// <summary>
    /// Forward WebRTC ICE Candidate between peers and cache candidates.
    /// </summary>
    public async Task SendIceCandidate(string sessionId, string candidateJson)
    {
        _cachedCandidates.GetOrAdd(sessionId, _ => new ConcurrentBag<string>()).Add(candidateJson);
        var groupName = $"session-{sessionId}";
        await Clients.OthersInGroup(groupName).SendAsync("ReceiveIceCandidate", sessionId, candidateJson);
    }

    /// <summary>
    /// Notification that Android screen sharing has started.
    /// </summary>
    public async Task NotifySessionStarted(string sessionId)
    {
        if (Guid.TryParse(sessionId, out var guid))
        {
            var status = await _screenSharingService.GetSessionStatusAsync(guid);
            await Clients.Group("Admins").SendAsync("ScreenSharingStarted", new
            {
                SessionId = sessionId,
                Status = status?.Status ?? "ACTIVE",
                Timestamp = DateTime.UtcNow
            });
        }
    }

    /// <summary>
    /// Notification that Android screen sharing has stopped.
    /// </summary>
    public async Task NotifySessionStopped(string sessionId)
    {
        _cachedOffers.TryRemove(sessionId, out _);
        _cachedCandidates.TryRemove(sessionId, out _);

        var groupName = $"session-{sessionId}";
        await Clients.Group(groupName).SendAsync("ScreenSharingStopped", sessionId);
        await Clients.Group("Admins").SendAsync("ScreenSharingStopped", sessionId);
    }

    /// <summary>
    /// Update peer connection state (Connecting, Connected, Reconnecting, Disconnected, etc.).
    /// </summary>
    public async Task UpdateConnectionState(string sessionId, string state)
    {
        if (Guid.TryParse(sessionId, out var guid))
        {
            await _screenSharingService.UpdateConnectionStateAsync(guid, state);
        }

        var groupName = $"session-{sessionId}";
        await Clients.Group(groupName).SendAsync("ConnectionStateChanged", sessionId, state);
        await Clients.Group("Admins").SendAsync("ConnectionStateChanged", sessionId, state);
    }

    /// <summary>
    /// Admin forcefully terminates an active screen sharing session.
    /// </summary>
    [Authorize(Roles = UserRoles.Admin)]
    public async Task AdminTerminateSession(string sessionId, string reason)
    {
        _cachedOffers.TryRemove(sessionId, out _);
        _cachedCandidates.TryRemove(sessionId, out _);

        var adminIdStr = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (Guid.TryParse(sessionId, out var sessionGuid) && Guid.TryParse(adminIdStr, out var adminGuid))
        {
            var ipAddress = Context.GetHttpContext()?.Connection.RemoteIpAddress?.ToString();
            await _screenSharingService.TerminateSessionByAdminAsync(adminGuid, sessionGuid, reason, ipAddress);

            var groupName = $"session-{sessionId}";
            // Force Android to stop screen capture immediately
            await Clients.Group(groupName).SendAsync("ForceStopSession", sessionId, reason);
            await Clients.Group("Admins").SendAsync("ScreenSharingStopped", sessionId);
        }
    }
}
