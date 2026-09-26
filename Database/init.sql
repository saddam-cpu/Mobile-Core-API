-- ====================================================================
-- Remote Screen Sharing & Admin Monitoring System
-- Production Database Initialization Script for Microsoft SQL Server
-- ====================================================================

IF NOT EXISTS (SELECT * FROM sys.databases WHERE name = 'ScreenSharingDb')
BEGIN
    CREATE DATABASE ScreenSharingDb;
END
GO

USE ScreenSharingDb;
GO

-- 1. Users Table
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Users')
BEGIN
    CREATE TABLE Users (
        UserId UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
        Name NVARCHAR(100) NOT NULL,
        Email NVARCHAR(150) NOT NULL,
        Mobile NVARCHAR(25) NULL,
        PasswordHash NVARCHAR(MAX) NOT NULL,
        Role NVARCHAR(20) NOT NULL DEFAULT 'USER',
        Status NVARCHAR(20) NOT NULL DEFAULT 'Active',
        CreatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        UpdatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        CONSTRAINT UQ_Users_Email UNIQUE (Email)
    );

    CREATE INDEX IX_Users_Mobile ON Users(Mobile);
END
GO

-- 2. Devices Table
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Devices')
BEGIN
    CREATE TABLE Devices (
        DeviceId NVARCHAR(100) NOT NULL PRIMARY KEY,
        UserId UNIQUEIDENTIFIER NOT NULL,
        DeviceName NVARCHAR(100) NOT NULL,
        AndroidVersion NVARCHAR(20) NULL,
        AppVersion NVARCHAR(20) NULL,
        DeviceModel NVARCHAR(100) NULL,
        DeviceStatus NVARCHAR(20) NOT NULL DEFAULT 'OFFLINE',
        LastSeenAt DATETIME2 NULL,
        CreatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_Devices_Users FOREIGN KEY (UserId) REFERENCES Users(UserId) ON DELETE CASCADE
    );

    CREATE INDEX IX_Devices_UserId ON Devices(UserId);
    CREATE INDEX IX_Devices_Status ON Devices(DeviceStatus);
END
GO

-- 3. ScreenSharingSessions Table
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ScreenSharingSessions')
BEGIN
    CREATE TABLE ScreenSharingSessions (
        SessionId UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
        UserId UNIQUEIDENTIFIER NOT NULL,
        DeviceId NVARCHAR(100) NOT NULL,
        StartedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        EndedAt DATETIME2 NULL,
        Status NVARCHAR(30) NOT NULL DEFAULT 'ACTIVE',
        ConnectionState NVARCHAR(30) NOT NULL DEFAULT 'NEW',
        IPAddress NVARCHAR(50) NULL,
        AppVersion NVARCHAR(20) NULL,
        CreatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_Sessions_Users FOREIGN KEY (UserId) REFERENCES Users(UserId),
        CONSTRAINT FK_Sessions_Devices FOREIGN KEY (DeviceId) REFERENCES Devices(DeviceId) ON DELETE CASCADE
    );

    CREATE INDEX IX_Sessions_UserId ON ScreenSharingSessions(UserId);
    CREATE INDEX IX_Sessions_DeviceId ON ScreenSharingSessions(DeviceId);
    CREATE INDEX IX_Sessions_Status ON ScreenSharingSessions(Status);
END
GO

-- 4. AdminUsers Table
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'AdminUsers')
BEGIN
    CREATE TABLE AdminUsers (
        AdminId UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
        Name NVARCHAR(100) NOT NULL,
        Email NVARCHAR(150) NOT NULL,
        PasswordHash NVARCHAR(MAX) NOT NULL,
        Role NVARCHAR(20) NOT NULL DEFAULT 'ADMIN',
        Status NVARCHAR(20) NOT NULL DEFAULT 'Active',
        CreatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        UpdatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        CONSTRAINT UQ_AdminUsers_Email UNIQUE (Email)
    );
END
GO

-- 5. AuditLogs Table
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'AuditLogs')
BEGIN
    CREATE TABLE AuditLogs (
        AuditId UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
        AdminId UNIQUEIDENTIFIER NULL,
        UserId UNIQUEIDENTIFIER NULL,
        Action NVARCHAR(100) NOT NULL,
        Timestamp DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        IPAddress NVARCHAR(50) NULL,
        Details NVARCHAR(1000) NULL,
        CONSTRAINT FK_AuditLogs_AdminUsers FOREIGN KEY (AdminId) REFERENCES AdminUsers(AdminId) ON DELETE SET NULL,
        CONSTRAINT FK_AuditLogs_Users FOREIGN KEY (UserId) REFERENCES Users(UserId) ON DELETE SET NULL
    );

    CREATE INDEX IX_AuditLogs_Timestamp ON AuditLogs(Timestamp);
    CREATE INDEX IX_AuditLogs_Action ON AuditLogs(Action);
END
GO

-- 6. RefreshTokens Table
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'RefreshTokens')
BEGIN
    CREATE TABLE RefreshTokens (
        Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
        Token NVARCHAR(200) NOT NULL,
        UserId UNIQUEIDENTIFIER NULL,
        AdminId UNIQUEIDENTIFIER NULL,
        ExpiresAt DATETIME2 NOT NULL,
        IsRevoked BIT NOT NULL DEFAULT 0,
        CreatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        ReplacedByToken NVARCHAR(200) NULL,
        CONSTRAINT UQ_RefreshTokens_Token UNIQUE (Token),
        CONSTRAINT FK_RefreshTokens_Users FOREIGN KEY (UserId) REFERENCES Users(UserId) ON DELETE CASCADE,
        CONSTRAINT FK_RefreshTokens_AdminUsers FOREIGN KEY (AdminId) REFERENCES AdminUsers(AdminId) ON DELETE CASCADE
    );
END
GO

-- 7. Seed Initial Administrator (admin@monitoring.local / Admin@123456)
IF NOT EXISTS (SELECT * FROM AdminUsers WHERE Email = 'admin@monitoring.local')
BEGIN
    INSERT INTO AdminUsers (AdminId, Name, Email, PasswordHash, Role, Status, CreatedAt, UpdatedAt)
    VALUES (
        '11111111-1111-1111-1111-111111111111',
        'System Administrator',
        'admin@monitoring.local',
        '$2a$11$N9qo8uLOickgx2ZMRZoMyeIjZAgcfl7p92ldGxad68LJZdL17lhWy',
        'ADMIN',
        'Active',
        SYSUTCDATETIME(),
        SYSUTCDATETIME()
    );
END
GO
