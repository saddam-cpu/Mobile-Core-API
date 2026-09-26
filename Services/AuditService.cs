using ScreenSharing.Api.Data;
using ScreenSharing.Api.Models;

namespace ScreenSharing.Api.Services;

public interface IAuditService
{
    Task LogAsync(string action, string details, Guid? userId = null, Guid? adminId = null, string? ipAddress = null);
}

public class AuditService : IAuditService
{
    private readonly AppDbContext _context;
    private readonly ILogger<AuditService> _logger;

    public AuditService(AppDbContext context, ILogger<AuditService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task LogAsync(string action, string details, Guid? userId = null, Guid? adminId = null, string? ipAddress = null)
    {
        try
        {
            var log = new AuditLog
            {
                AuditId = Guid.NewGuid(),
                Action = action,
                Details = details,
                UserId = userId,
                AdminId = adminId,
                IPAddress = ipAddress ?? string.Empty,
                Timestamp = DateTime.UtcNow
            };

            _context.AuditLogs.Add(log);
            await _context.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to record audit log for action: {Action}", action);
        }
    }
}
