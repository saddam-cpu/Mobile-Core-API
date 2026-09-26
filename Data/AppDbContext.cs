using Microsoft.EntityFrameworkCore;
using ScreenSharing.Api.Models;

namespace ScreenSharing.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<ScreenSharingSession> ScreenSharingSessions => Set<ScreenSharingSession>();
    public DbSet<AdminUser> AdminUsers => Set<AdminUser>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // User indexes
        modelBuilder.Entity<User>()
            .HasIndex(u => u.Email)
            .IsUnique();

        modelBuilder.Entity<User>()
            .HasIndex(u => u.Mobile);

        // Device indexes & keys
        modelBuilder.Entity<Device>()
            .HasKey(d => d.DeviceId);

        modelBuilder.Entity<Device>()
            .HasOne(d => d.User)
            .WithMany(u => u.Devices)
            .HasForeignKey(d => d.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // ScreenSharingSessions relations
        modelBuilder.Entity<ScreenSharingSession>()
            .HasOne(s => s.User)
            .WithMany(u => u.Sessions)
            .HasForeignKey(s => s.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<ScreenSharingSession>()
            .HasOne(s => s.Device)
            .WithMany(d => d.Sessions)
            .HasForeignKey(s => s.DeviceId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ScreenSharingSession>()
            .HasIndex(s => s.Status);

        // AdminUser indexes
        modelBuilder.Entity<AdminUser>()
            .HasIndex(a => a.Email)
            .IsUnique();

        // AuditLog relations
        modelBuilder.Entity<AuditLog>()
            .HasOne(a => a.Admin)
            .WithMany(u => u.AuditLogs)
            .HasForeignKey(a => a.AdminId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<AuditLog>()
            .HasOne(a => a.User)
            .WithMany()
            .HasForeignKey(a => a.UserId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<AuditLog>()
            .HasIndex(a => a.Timestamp);

        // RefreshTokens index
        modelBuilder.Entity<RefreshToken>()
            .HasIndex(r => r.Token)
            .IsUnique();

        // Seed Default Admin User: admin@monitoring.local / Admin@123456
        var defaultAdminId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        // Hash for "Admin@123456" generated via BCrypt
        var adminPasswordHash = "$2a$11$N9qo8uLOickgx2ZMRZoMyeIjZAgcfl7p92ldGxad68LJZdL17lhWy";

        modelBuilder.Entity<AdminUser>().HasData(new AdminUser
        {
            AdminId = defaultAdminId,
            Name = "System Administrator",
            Email = "admin@monitoring.local",
            PasswordHash = adminPasswordHash,
            Role = UserRoles.Admin,
            Status = "Active",
            CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        });
    }
}
