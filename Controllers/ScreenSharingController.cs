using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ScreenSharing.Api.DTOs;
using ScreenSharing.Api.Services;

namespace ScreenSharing.Api.Controllers;

[ApiController]
[Route("api/screen-sharing")]
[Authorize]
public class ScreenSharingController : ControllerBase
{
    private readonly IScreenSharingService _screenSharingService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ScreenSharingController> _logger;

    public ScreenSharingController(
        IScreenSharingService screenSharingService,
        IConfiguration configuration,
        ILogger<ScreenSharingController> logger)
    {
        _screenSharingService = screenSharingService;
        _configuration = configuration;
        _logger = logger;
    }

    private Guid GetCurrentUserId()
    {
        var idStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(idStr, out var id) ? id : Guid.Empty;
    }

    [HttpPost("session/start")]
    public async Task<ActionResult<SessionDto>> StartSession([FromBody] StartSessionRequest request)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty)
            return Unauthorized();

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var session = await _screenSharingService.StartSessionAsync(userId, request, ip);

        return Ok(session);
    }

    [HttpPost("session/stop")]
    public async Task<IActionResult> StopSession([FromBody] StopSessionRequest request)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty)
            return Unauthorized();

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var result = await _screenSharingService.StopSessionAsync(userId, request, ip);

        if (!result)
            return NotFound(new { message = "Active session not found" });

        return Ok(new { message = "Session stopped successfully", sessionId = request.SessionId });
    }

    [HttpGet("session/{sessionId}/status")]
    public async Task<ActionResult<SessionStatusResponse>> GetSessionStatus(Guid sessionId)
    {
        var status = await _screenSharingService.GetSessionStatusAsync(sessionId);
        if (status == null)
            return NotFound(new { message = "Session not found" });

        return Ok(status);
    }

    [HttpGet("sessions")]
    public async Task<ActionResult<List<SessionDto>>> GetUserSessions([FromQuery] int limit = 20)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty)
            return Unauthorized();

        var sessions = await _screenSharingService.GetUserSessionsAsync(userId, limit);
        return Ok(sessions);
    }

    [HttpGet("config/ice-servers")]
    [AllowAnonymous] // Android and Admin viewers need ICE server config
    public ActionResult<WebRtcConfigResponse> GetIceServers()
    {
        var stunServer = _configuration["WebRtc:StunServer"] ?? "stun:stun.l.google.com:19302";
        var stunServerBackup = _configuration["WebRtc:StunServerBackup"] ?? "stun:stun1.l.google.com:19302";
        var turnServer = _configuration["WebRtc:TurnServer"];
        var turnUsername = _configuration["WebRtc:TurnUsername"];
        var turnPassword = _configuration["WebRtc:TurnPassword"];

        var response = new WebRtcConfigResponse();

        // Add STUN servers
        response.IceServers.Add(new IceServerConfigDto
        {
            Urls = new List<string> { stunServer, stunServerBackup }
        });

        // Add TURN server if configured
        if (!string.IsNullOrEmpty(turnServer))
        {
            response.IceServers.Add(new IceServerConfigDto
            {
                Urls = new List<string> { turnServer },
                Username = turnUsername,
                Credential = turnPassword
            });
        }

        // Detect host LAN IP
        try
        {
            var hostIp = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up &&
                            n.NetworkInterfaceType != System.Net.NetworkInformation.NetworkInterfaceType.Loopback)
                .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                .FirstOrDefault(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)?.Address.ToString();

            response.HostIp = hostIp ?? "172.16.84.6";
        }
        catch
        {
            response.HostIp = "172.16.84.6";
        }

        return Ok(response);
    }
}
