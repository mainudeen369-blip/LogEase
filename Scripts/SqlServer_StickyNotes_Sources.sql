-- Sticky notes + GitLab/mail connector columns (Phase A / B)
-- Safe to re-run.

IF OBJECT_ID(N'dbo.logease_StickyNotes', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[logease_StickyNotes] (
        [Id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
        [UserId] UNIQUEIDENTIFIER NOT NULL,
        [NoteDate] DATE NOT NULL,
        [Body] NVARCHAR(4000) NOT NULL,
        [CreatedAt] DATETIME2 NOT NULL CONSTRAINT [DF_logease_StickyNotes_CreatedAt] DEFAULT (SYSUTCDATETIME()),
        [UpdatedAt] DATETIME2 NOT NULL CONSTRAINT [DF_logease_StickyNotes_UpdatedAt] DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT [FK_logease_StickyNotes_Users] FOREIGN KEY ([UserId]) REFERENCES [dbo].[logease_Users]([Id]) ON DELETE CASCADE
    );
    CREATE INDEX [IX_logease_StickyNotes_UserId_NoteDate]
        ON [dbo].[logease_StickyNotes]([UserId], [NoteDate]);
END
GO

IF COL_LENGTH(N'dbo.logease_Users', N'GitlabPersonalAccessToken') IS NULL
    ALTER TABLE [dbo].[logease_Users] ADD [GitlabPersonalAccessToken] NVARCHAR(512) NULL;
IF COL_LENGTH(N'dbo.logease_Users', N'GitlabBaseUrl') IS NULL
    ALTER TABLE [dbo].[logease_Users] ADD [GitlabBaseUrl] NVARCHAR(200) NULL;
IF COL_LENGTH(N'dbo.logease_Users', N'GitlabUsername') IS NULL
    ALTER TABLE [dbo].[logease_Users] ADD [GitlabUsername] NVARCHAR(120) NULL;
IF COL_LENGTH(N'dbo.logease_Users', N'MailImapHost') IS NULL
    ALTER TABLE [dbo].[logease_Users] ADD [MailImapHost] NVARCHAR(200) NULL;
IF COL_LENGTH(N'dbo.logease_Users', N'MailImapPort') IS NULL
    ALTER TABLE [dbo].[logease_Users] ADD [MailImapPort] INT NULL;
IF COL_LENGTH(N'dbo.logease_Users', N'MailImapUser') IS NULL
    ALTER TABLE [dbo].[logease_Users] ADD [MailImapUser] NVARCHAR(200) NULL;
IF COL_LENGTH(N'dbo.logease_Users', N'MailImapPassword') IS NULL
    ALTER TABLE [dbo].[logease_Users] ADD [MailImapPassword] NVARCHAR(512) NULL;
IF COL_LENGTH(N'dbo.logease_Users', N'MailImapUseSsl') IS NULL
    ALTER TABLE [dbo].[logease_Users] ADD [MailImapUseSsl] BIT NOT NULL CONSTRAINT [DF_logease_Users_MailImapUseSsl] DEFAULT (1);
GO
