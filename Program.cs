using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using ScreenSharing.Api.Authentication;
using ScreenSharing.Api.Data;
using ScreenSharing.Api.Hubs;
using ScreenSharing.Api.Middleware;
using ScreenSharing.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// Dynamic PORT binding for Render / Cloud hosting
var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrEmpty(port))
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
}

// 1. Add Controllers & JSON Options
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    });

builder.Services.AddEndpointsApiExplorer();

// 2. Swagger / OpenAPI with JWT Bearer Support
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Screen Sharing & Admin Monitoring API",
        Version = "v1",
        Description = "Production API for Android Remote Screen Sharing, Signaling, and Admin Telemetry."
    });

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme. Example: \"Bearer {token}\"",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

// 3. Database Context Configuration
var useSqlite = builder.Configuration.GetValue<bool>("UseSqlite", true);
if (useSqlite)
{
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? "Data Source=screensharing.db";
    builder.Services.AddDbContext<AppDbContext>(options =>
        options.UseSqlite(connectionString));
}
else
{
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
    builder.Services.AddDbContext<AppDbContext>(options =>
        options.UseSqlServer(connectionString, sqlOptions =>
        {
            sqlOptions.EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(10), errorNumbersToAdd: null);
        }));
}

// 4. Dependency Injection
builder.Services.AddScoped<IPasswordHasher, PasswordHasher>();
builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();
builder.Services.AddScoped<IAuditService, AuditService>();
builder.Services.AddScoped<IScreenSharingService, ScreenSharingService>();

// 5. SignalR for WebRTC Signaling & Telemetry
builder.Services.AddSignalR(options =>
{
    options.EnableDetailedErrors = true;
    options.KeepAliveInterval = TimeSpan.FromSeconds(15);
    options.ClientTimeoutInterval = TimeSpan.FromSeconds(30);
});

// 6. JWT Authentication Configuration
var secretKey = builder.Configuration["Jwt:SecretKey"] ?? "SUPER_SECURE_JWT_SIGNING_KEY_SCREEN_SHARING_2026_PRODUCTION_COMPLIANT_MIN_256_BITS!";
var issuer = builder.Configuration["Jwt:Issuer"] ?? "ScreenSharingApi";
var audience = builder.Configuration["Jwt:Audience"] ?? "ScreenSharingClients";

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.RequireHttpsMetadata = false; // Set to true in strict production HTTPS environments
    options.SaveToken = true;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey)),
        ValidateIssuer = true,
        ValidIssuer = issuer,
        ValidateAudience = true,
        ValidAudience = audience,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.Zero
    };

    // Support JWT tokens over SignalR WebSockets via query parameter
    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            var accessToken = context.Request.Query["access_token"];
            var path = context.HttpContext.Request.Path;
            if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
            {
                context.Token = accessToken;
            }
            return Task.CompletedTask;
        }
    };
});

builder.Services.AddAuthorization();

// 7. CORS Configuration
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAllOrigins", policy =>
    {
        policy.SetIsOriginAllowed(_ => true)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

var app = builder.Build();

// 8. Ensure Database created and seeded
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
    dbContext.Database.EnsureCreated();

    var admin = dbContext.AdminUsers.FirstOrDefault(a => a.Email == "admin@monitoring.local");
    if (admin == null)
    {
        dbContext.AdminUsers.Add(new ScreenSharing.Api.Models.AdminUser
        {
            AdminId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Name = "System Administrator",
            Email = "admin@monitoring.local",
            PasswordHash = passwordHasher.HashPassword("Admin@123456"),
            Role = ScreenSharing.Api.Models.UserRoles.Admin,
            Status = "Active",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        dbContext.SaveChanges();
    }
    else
    {
        admin.PasswordHash = passwordHasher.HashPassword("Admin@123456");
        dbContext.SaveChanges();
    }

    var user = dbContext.Users.FirstOrDefault(u => u.Email == "john.doe@example.com");
    if (user == null)
    {
        dbContext.Users.Add(new ScreenSharing.Api.Models.User
        {
            UserId = Guid.Parse("2acde47c-47aa-44c9-b2e9-230207e162f6"),
            Name = "John Doe",
            Email = "john.doe@example.com",
            Mobile = "+1234567890",
            PasswordHash = passwordHasher.HashPassword("Password@123"),
            Role = ScreenSharing.Api.Models.UserRoles.User,
            Status = "Active",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        dbContext.SaveChanges();
    }

    // Clean up any stale sessions left in ACTIVE status from previous server runs
    var staleSessions = dbContext.ScreenSharingSessions.Where(s => s.Status == "ACTIVE").ToList();
    foreach (var s in staleSessions)
    {
        s.Status = "STOPPED";
        s.EndedAt = DateTime.UtcNow;
        s.ConnectionState = "CLOSED";
    }
    dbContext.SaveChanges();
}

// 9. Middleware Pipeline
app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseMiddleware<SecurityHeadersMiddleware>();

// Enable Swagger in all environments for external mobile & developer testing
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Screen Sharing API v1");
    c.RoutePrefix = "swagger";
});

app.UseCors("AllowAllOrigins");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<SignalingHub>("/hubs/signaling");

// Health check endpoint
app.MapGet("/", () => Results.Ok(new
{
    service = "Remote Screen Sharing & Monitoring API",
    status = "Healthy",
    version = "1.0.0",
    timestamp = DateTime.UtcNow
}));

app.Run();
