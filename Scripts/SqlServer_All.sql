/*
  LogEase — all SQL in one file (Microsoft SQL Server)
  Tables use prefix logease_ on dbo (no custom schema).

  Run on your test database (e.g. Database=test).
*/

SET NOCOUNT ON;
GO

/* ---------- Tables ---------- */

IF OBJECT_ID(N'[dbo].[logease_Users]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[logease_Users]
    (
        [Id]               UNIQUEIDENTIFIER NOT NULL CONSTRAINT [PK_logease_Users] PRIMARY KEY,
        [Email]            NVARCHAR(200)    NOT NULL,
        [Name]             NVARCHAR(120)    NOT NULL,
        [PasswordHash]     NVARCHAR(MAX)    NOT NULL,
        [ReminderTime]     TIME(7)          NOT NULL CONSTRAINT [DF_logease_Users_ReminderTime] DEFAULT ('17:45:00'),
        [ReminderEnabled]  BIT              NOT NULL CONSTRAINT [DF_logease_Users_ReminderEnabled] DEFAULT (1),
        [TimeZoneId]       NVARCHAR(80)     NOT NULL CONSTRAINT [DF_logease_Users_TimeZoneId] DEFAULT (N'India Standard Time'),
        [KimaiApiToken]    NVARCHAR(512)    NULL,
        [AzureObjectId]    NVARCHAR(64)     NULL,
        [ProfilePhotoUrl]  NVARCHAR(512)    NULL,
        [AiParseInstructions] NVARCHAR(4000) NULL,
        [CreatedAt]        DATETIME2(7)     NOT NULL CONSTRAINT [DF_logease_Users_CreatedAt] DEFAULT (SYSUTCDATETIME())
    );

    CREATE UNIQUE INDEX [IX_logease_Users_Email] ON [dbo].[logease_Users]([Email]);
    CREATE UNIQUE NONCLUSTERED INDEX [IX_logease_Users_AzureObjectId]
        ON [dbo].[logease_Users]([AzureObjectId])
        WHERE [AzureObjectId] IS NOT NULL;
END
GO

/* ---------- Upgrade existing logease_Users (Azure AD + AI wording) ---------- */

IF COL_LENGTH(N'dbo.logease_Users', N'AzureObjectId') IS NULL
BEGIN
    ALTER TABLE [dbo].[logease_Users] ADD [AzureObjectId] NVARCHAR(64) NULL;
    PRINT 'Added AzureObjectId';
END
ELSE
    PRINT 'AzureObjectId already exists';
GO

IF COL_LENGTH(N'dbo.logease_Users', N'ProfilePhotoUrl') IS NULL
BEGIN
    ALTER TABLE [dbo].[logease_Users] ADD [ProfilePhotoUrl] NVARCHAR(512) NULL;
    PRINT 'Added ProfilePhotoUrl';
END
ELSE
    PRINT 'ProfilePhotoUrl already exists';
GO

IF COL_LENGTH(N'dbo.logease_Users', N'AiParseInstructions') IS NULL
BEGIN
    ALTER TABLE [dbo].[logease_Users] ADD [AiParseInstructions] NVARCHAR(4000) NULL;
    PRINT 'Added AiParseInstructions';
END
ELSE
    PRINT 'AiParseInstructions already exists';
GO

IF OBJECT_ID(N'dbo.logease_Users', N'U') IS NOT NULL
   AND NOT EXISTS (
        SELECT 1 FROM sys.indexes
        WHERE name = N'IX_logease_Users_AzureObjectId'
          AND object_id = OBJECT_ID(N'dbo.logease_Users'))
BEGIN
    CREATE UNIQUE NONCLUSTERED INDEX [IX_logease_Users_AzureObjectId]
        ON [dbo].[logease_Users]([AzureObjectId])
        WHERE [AzureObjectId] IS NOT NULL;
    PRINT 'Created IX_logease_Users_AzureObjectId';
END
GO

IF OBJECT_ID(N'[dbo].[logease_Projects]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[logease_Projects]
    (
        [Id]       UNIQUEIDENTIFIER NOT NULL CONSTRAINT [PK_logease_Projects] PRIMARY KEY,
        [Name]     NVARCHAR(160)    NOT NULL,
        [IsActive] BIT              NOT NULL CONSTRAINT [DF_logease_Projects_IsActive] DEFAULT (1)
    );
END
GO

IF OBJECT_ID(N'[dbo].[logease_Activities]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[logease_Activities]
    (
        [Id]        UNIQUEIDENTIFIER NOT NULL CONSTRAINT [PK_logease_Activities] PRIMARY KEY,
        [ProjectId] UNIQUEIDENTIFIER NOT NULL,
        [Name]      NVARCHAR(120)    NOT NULL,
        CONSTRAINT [FK_logease_Activities_Projects]
            FOREIGN KEY ([ProjectId]) REFERENCES [dbo].[logease_Projects]([Id]) ON DELETE CASCADE
    );

    CREATE INDEX [IX_logease_Activities_ProjectId] ON [dbo].[logease_Activities]([ProjectId]);
END
GO

IF OBJECT_ID(N'[dbo].[logease_Worklogs]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[logease_Worklogs]
    (
        [Id]          UNIQUEIDENTIFIER NOT NULL CONSTRAINT [PK_logease_Worklogs] PRIMARY KEY,
        [UserId]      UNIQUEIDENTIFIER NOT NULL,
        [ProjectId]   UNIQUEIDENTIFIER NOT NULL,
        [ActivityId]  UNIQUEIDENTIFIER NOT NULL,
        [WorkDate]    DATE             NOT NULL,
        [Hours]       DECIMAL(5,2)     NOT NULL,
        [Description] NVARCHAR(2000)   NOT NULL,
        [CreatedAt]   DATETIME2(7)     NOT NULL CONSTRAINT [DF_logease_Worklogs_CreatedAt] DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT [FK_logease_Worklogs_Users]
            FOREIGN KEY ([UserId]) REFERENCES [dbo].[logease_Users]([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_logease_Worklogs_Projects]
            FOREIGN KEY ([ProjectId]) REFERENCES [dbo].[logease_Projects]([Id]),
        CONSTRAINT [FK_logease_Worklogs_Activities]
            FOREIGN KEY ([ActivityId]) REFERENCES [dbo].[logease_Activities]([Id])
    );

    CREATE INDEX [IX_logease_Worklogs_UserId_WorkDate]
        ON [dbo].[logease_Worklogs]([UserId], [WorkDate]);
END
GO

IF COL_LENGTH(N'dbo.logease_Users', N'KimaiApiToken') IS NULL
BEGIN
    ALTER TABLE [dbo].[logease_Users]
        ADD [KimaiApiToken] NVARCHAR(512) NULL;
END
GO

IF COL_LENGTH(N'dbo.logease_Users', N'AiParseInstructions') IS NULL
BEGIN
    ALTER TABLE [dbo].[logease_Users]
        ADD [AiParseInstructions] NVARCHAR(4000) NULL;
END
GO

IF OBJECT_ID(N'[dbo].[logease_AppSettings]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[logease_AppSettings]
    (
        [Key]       NVARCHAR(80)  NOT NULL CONSTRAINT [PK_logease_AppSettings] PRIMARY KEY,
        [Value]     NVARCHAR(200) NOT NULL,
        [UpdatedAt] DATETIME2(7)  NOT NULL CONSTRAINT [DF_logease_AppSettings_UpdatedAt] DEFAULT (SYSUTCDATETIME())
    );
END
GO

/* ---------- Seed (demo) ---------- */
/* Login: demo@logease.local / Demo@123 */

DECLARE @UserId UNIQUEIDENTIFIER = '11111111-1111-1111-1111-111111111111';
DECLARE @PwdHash NVARCHAR(200) = N'$2a$11$i8N/hTRPhTPUPZYVqxh3L.tJKHu4jVXHfoRzyLwT21cXne7DvlUV.';

IF NOT EXISTS (SELECT 1 FROM [dbo].[logease_Users] WHERE [Id] = @UserId)
BEGIN
    INSERT INTO [dbo].[logease_Users]
        ([Id], [Email], [Name], [PasswordHash], [ReminderTime], [ReminderEnabled], [TimeZoneId], [CreatedAt])
    VALUES
        (@UserId, N'demo@logease.local', N'Demo User', @PwdHash, '17:45:00', 1, N'India Standard Time', SYSUTCDATETIME());
END
GO

DECLARE @P1 UNIQUEIDENTIFIER = '22222222-2222-2222-2222-222222222201';
DECLARE @P2 UNIQUEIDENTIFIER = '22222222-2222-2222-2222-222222222202';
DECLARE @P3 UNIQUEIDENTIFIER = '22222222-2222-2222-2222-222222222203';
DECLARE @P4 UNIQUEIDENTIFIER = '22222222-2222-2222-2222-222222222204';
DECLARE @P5 UNIQUEIDENTIFIER = '22222222-2222-2222-2222-222222222205';
DECLARE @P6 UNIQUEIDENTIFIER = '22222222-2222-2222-2222-222222222206';

IF NOT EXISTS (SELECT 1 FROM [dbo].[logease_Projects] WHERE [Id] = @P1)
BEGIN
    INSERT INTO [dbo].[logease_Projects] ([Id], [Name], [IsActive]) VALUES
        (@P1, N'Perception Survey', 1),
        (@P2, N'Code Sharing Tool', 1),
        (@P3, N'LogEase', 1),
        (@P4, N'Internal Support', 1),
        (@P5, N'Training / Learning', 1),
        (@P6, N'Company Announcement', 1);
END
ELSE IF NOT EXISTS (SELECT 1 FROM [dbo].[logease_Projects] WHERE [Id] = @P6)
BEGIN
    INSERT INTO [dbo].[logease_Projects] ([Id], [Name], [IsActive])
    VALUES (@P6, N'Company Announcement', 1);
END
GO

DECLARE @P1 UNIQUEIDENTIFIER = '22222222-2222-2222-2222-222222222201';
DECLARE @P2 UNIQUEIDENTIFIER = '22222222-2222-2222-2222-222222222202';
DECLARE @P3 UNIQUEIDENTIFIER = '22222222-2222-2222-2222-222222222203';
DECLARE @P4 UNIQUEIDENTIFIER = '22222222-2222-2222-2222-222222222204';
DECLARE @P5 UNIQUEIDENTIFIER = '22222222-2222-2222-2222-222222222205';
DECLARE @P6 UNIQUEIDENTIFIER = '22222222-2222-2222-2222-222222222206';

IF NOT EXISTS (SELECT 1 FROM [dbo].[logease_Activities] WHERE [ProjectId] = @P1)
BEGIN
    INSERT INTO [dbo].[logease_Activities] ([Id], [ProjectId], [Name]) VALUES
        ('33333333-3333-3333-3333-333333333101', @P1, N'Development'),
        ('33333333-3333-3333-3333-333333333102', @P1, N'Bug Fix'),
        ('33333333-3333-3333-3333-333333333103', @P1, N'Meeting'),

        ('33333333-3333-3333-3333-333333333201', @P2, N'Development'),
        ('33333333-3333-3333-3333-333333333202', @P2, N'Support'),
        ('33333333-3333-3333-3333-333333333203', @P2, N'Meeting'),

        ('33333333-3333-3333-3333-333333333301', @P3, N'Development'),
        ('33333333-3333-3333-3333-333333333302', @P3, N'Demo Prep'),
        ('33333333-3333-3333-3333-333333333303', @P3, N'Meeting'),

        ('33333333-3333-3333-3333-333333333401', @P4, N'Support'),
        ('33333333-3333-3333-3333-333333333402', @P4, N'Documentation'),
        ('33333333-3333-3333-3333-333333333403', @P4, N'Meeting'),

        ('33333333-3333-3333-3333-333333333501', @P5, N'Learning'),
        ('33333333-3333-3333-3333-333333333502', @P5, N'Documentation'),
        ('33333333-3333-3333-3333-333333333503', @P5, N'Meeting'),

        ('33333333-3333-3333-3333-333333333601', @P6, N'Development'),
        ('33333333-3333-3333-3333-333333333602', @P6, N'Bug Fix'),
        ('33333333-3333-3333-3333-333333333603', @P6, N'Support');
END
ELSE IF NOT EXISTS (SELECT 1 FROM [dbo].[logease_Activities] WHERE [ProjectId] = @P6)
BEGIN
    INSERT INTO [dbo].[logease_Activities] ([Id], [ProjectId], [Name]) VALUES
        ('33333333-3333-3333-3333-333333333601', @P6, N'Development'),
        ('33333333-3333-3333-3333-333333333602', @P6, N'Bug Fix'),
        ('33333333-3333-3333-3333-333333333603', @P6, N'Support');
END
GO

PRINT N'LogEase tables ready (logease_*). Demo: demo@logease.local / Demo@123';
GO
