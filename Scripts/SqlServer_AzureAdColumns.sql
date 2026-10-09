-- Azure AD login columns for LogEase users
-- Run against the same database as LogEase.Api ConnectionStrings:DefaultConnection
-- (e.g. Server=10.10.23.4; Database=test)

USE [test]; -- change if your DB name differs
GO

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
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = N'IX_logease_Users_AzureObjectId'
      AND object_id = OBJECT_ID(N'dbo.logease_Users'))
BEGIN
    CREATE UNIQUE NONCLUSTERED INDEX [IX_logease_Users_AzureObjectId]
        ON [dbo].[logease_Users]([AzureObjectId])
        WHERE [AzureObjectId] IS NOT NULL;
    PRINT 'Created IX_logease_Users_AzureObjectId';
END
ELSE
    PRINT 'Index IX_logease_Users_AzureObjectId already exists';
GO

PRINT 'Done. Restart LogEase.Api, then try Sign in with Azure again.';
GO
