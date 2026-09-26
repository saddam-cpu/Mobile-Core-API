using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ScreenSharing.Api.Authentication;
using ScreenSharing.Api.Data;
using ScreenSharing.Api.DTOs;
using ScreenSharing.Api.Models;
using ScreenSharing.Api.Services;

namespace ScreenSharing.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly IAuditService _auditService;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        AppDbContext context,
        IPasswordHasher passwordHasher,
        IJwtTokenService jwtTokenService,
        IAuditService auditService,
        ILogger<AuthController> logger)
    {
        _context = context;
        _passwordHasher = passwordHasher;
        _jwtTokenService = jwtTokenService;
        _auditService = auditService;
        _logger = logger;
    }

    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthResponse>> Register([FromBody] RegisterRequest request)
    {
        if (!request.AgreeToTerms)
        {
            return BadRequest(new { message = "You must agree to the Terms & Privacy Policy to register." });
        }

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();

        if (await _context.Users.AnyAsync(u => u.Email.ToLower() == normalizedEmail))
        {
            return Conflict(new { message = "An account with this email address already exists." });
        }

        if (!string.IsNullOrWhiteSpace(request.Mobile) &&
            await _context.Users.AnyAsync(u => u.Mobile == request.Mobile.Trim()))
        {
            return Conflict(new { message = "An account with this mobile number already exists." });
        }

        var user = new User
        {
            UserId = Guid.NewGuid(),
            Name = request.FullName.Trim(),
            Email = normalizedEmail,
            Mobile = request.Mobile.Trim(),
            PasswordHash = _passwordHasher.HashPassword(request.Password),
            Role = UserRoles.User,
            Status = "Active",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _context.Users.Add(user);

        var (accessToken, expiresAt) = _jwtTokenService.GenerateAccessToken(user.UserId, user.Name, user.Email, user.Role);
        var refreshTokenValue = _jwtTokenService.GenerateRefreshToken();

        var refreshToken = new RefreshToken
        {
            Token = refreshTokenValue,
            UserId = user.UserId,
            ExpiresAt = DateTime.UtcNow.AddDays(365),
            CreatedAt = DateTime.UtcNow
        };

        _context.RefreshTokens.Add(refreshToken);
        await _context.SaveChangesAsync();

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        await _auditService.LogAsync("USER_REGISTER", $"User registered: {user.Email}", userId: user.UserId, ipAddress: ip);

        return Ok(new AuthResponse
        {
            AccessToken = accessToken,
            RefreshToken = refreshTokenValue,
            ExpiresAt = expiresAt,
            UserId = user.UserId,
            Name = user.Name,
            Email = user.Email,
            Role = user.Role
        });
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthResponse>> Login([FromBody] LoginRequest request)
    {
        var identifier = request.Identifier.Trim().ToLowerInvariant();

        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Email.ToLower() == identifier || u.Mobile == request.Identifier.Trim());

        if (user == null || !_passwordHasher.VerifyPassword(request.Password, user.PasswordHash))
        {
            return Unauthorized(new { message = "Invalid email/mobile or password." });
        }

        if (user.Status != "Active")
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Account is inactive or suspended. Please contact admin." });
        }

        var (accessToken, expiresAt) = _jwtTokenService.GenerateAccessToken(user.UserId, user.Name, user.Email, user.Role);
        var refreshTokenValue = _jwtTokenService.GenerateRefreshToken();

        var refreshToken = new RefreshToken
        {
            Token = refreshTokenValue,
            UserId = user.UserId,
            ExpiresAt = DateTime.UtcNow.AddDays(365),
            CreatedAt = DateTime.UtcNow
        };

        _context.RefreshTokens.Add(refreshToken);
        await _context.SaveChangesAsync();

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        await _auditService.LogAsync("USER_LOGIN", $"User logged in: {user.Email}", userId: user.UserId, ipAddress: ip);

        return Ok(new AuthResponse
        {
            AccessToken = accessToken,
            RefreshToken = refreshTokenValue,
            ExpiresAt = expiresAt,
            UserId = user.UserId,
            Name = user.Name,
            Email = user.Email,
            Role = user.Role
        });
    }

    [HttpPost("refresh-token")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthResponse>> RefreshToken([FromBody] RefreshTokenRequest request)
    {
        try
        {
            var principal = _jwtTokenService.GetPrincipalFromExpiredToken(request.AccessToken);
            if (principal == null)
            {
                return BadRequest(new { message = "Invalid access token" });
            }

            var userIdClaim = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(userIdClaim, out var userId))
            {
                return BadRequest(new { message = "Invalid user token claims" });
            }

            var storedRefreshToken = await _context.RefreshTokens
                .Include(r => r.User)
                .Include(r => r.Admin)
                .FirstOrDefaultAsync(r => r.Token == request.RefreshToken);

            if (storedRefreshToken == null || !storedRefreshToken.IsActive)
            {
                return Unauthorized(new { message = "Refresh token is invalid or expired" });
            }

            // Revoke current token and generate new ones
            storedRefreshToken.IsRevoked = true;
            var newRefreshTokenValue = _jwtTokenService.GenerateRefreshToken();
            storedRefreshToken.ReplacedByToken = newRefreshTokenValue;

            string name, email, role;
            Guid entityId;

            if (storedRefreshToken.UserId.HasValue && storedRefreshToken.User != null)
            {
                entityId = storedRefreshToken.User.UserId;
                name = storedRefreshToken.User.Name;
                email = storedRefreshToken.User.Email;
                role = storedRefreshToken.User.Role;
            }
            else if (storedRefreshToken.AdminId.HasValue && storedRefreshToken.Admin != null)
            {
                entityId = storedRefreshToken.Admin.AdminId;
                name = storedRefreshToken.Admin.Name;
                email = storedRefreshToken.Admin.Email;
                role = storedRefreshToken.Admin.Role;
            }
            else
            {
                return Unauthorized(new { message = "Token entity not found" });
            }

            var (newAccessToken, expiresAt) = _jwtTokenService.GenerateAccessToken(entityId, name, email, role);

            var newRefreshToken = new RefreshToken
            {
                Token = newRefreshTokenValue,
                UserId = storedRefreshToken.UserId,
                AdminId = storedRefreshToken.AdminId,
                ExpiresAt = DateTime.UtcNow.AddDays(365),
                CreatedAt = DateTime.UtcNow
            };

            _context.RefreshTokens.Add(newRefreshToken);
            await _context.SaveChangesAsync();

            return Ok(new AuthResponse
            {
                AccessToken = newAccessToken,
                RefreshToken = newRefreshTokenValue,
                ExpiresAt = expiresAt,
                UserId = entityId,
                Name = name,
                Email = email,
                Role = role
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Refresh token error");
            return Unauthorized(new { message = "Invalid token credentials" });
        }
    }

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout([FromBody] RefreshTokenRequest request)
    {
        var storedRefreshToken = await _context.RefreshTokens
            .FirstOrDefaultAsync(r => r.Token == request.RefreshToken);

        if (storedRefreshToken != null)
        {
            storedRefreshToken.IsRevoked = true;
            await _context.SaveChangesAsync();
        }

        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (Guid.TryParse(userIdClaim, out var userId))
        {
            var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
            await _auditService.LogAsync("USER_LOGOUT", "User logged out", userId: userId, ipAddress: ip);
        }

        return Ok(new { message = "Logged out successfully" });
    }
}
