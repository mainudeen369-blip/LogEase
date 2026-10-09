/*
  LogEase — AI Ops tables (Observability / Incidents / Technical Debt)
  Safe to run on existing DBs (IF OBJECT_ID guards).
*/
IF OBJECT_ID(N'dbo.logease_AiParseEvents', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[logease_AiParseEvents] (
        [Id] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [PK_logease_AiParseEvents] PRIMARY KEY,
        [UserId] UNIQUEIDENTIFIER NULL,
        [Parser] NVARCHAR(80) NOT NULL,
        [RowCount] INT NOT NULL,
        [DurationMs] INT NOT NULL,
        [UsedFallback] BIT NOT NULL,
        [Success] BIT NOT NULL,
        [Warning] NVARCHAR(500) NULL,
        [Source] NVARCHAR(40) NOT NULL,
        [CreatedAt] DATETIME2(7) NOT NULL
    );
    CREATE INDEX [IX_logease_AiParseEvents_CreatedAt]
        ON [dbo].[logease_AiParseEvents]([CreatedAt]);
    CREATE INDEX [IX_logease_AiParseEvents_Parser]
        ON [dbo].[logease_AiParseEvents]([Parser]);
END
GO

IF OBJECT_ID(N'dbo.logease_OpsIncidents', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[logease_OpsIncidents] (
        [Id] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [PK_logease_OpsIncidents] PRIMARY KEY,
        [Title] NVARCHAR(200) NOT NULL,
        [Detail] NVARCHAR(2000) NOT NULL,
        [Severity] NVARCHAR(20) NOT NULL,
        [Status] NVARCHAR(30) NOT NULL,
        [Category] NVARCHAR(40) NOT NULL,
        [RelatedParseEventId] UNIQUEIDENTIFIER NULL,
        [CreatedByUserId] UNIQUEIDENTIFIER NULL,
        [CreatedAt] DATETIME2(7) NOT NULL,
        [UpdatedAt] DATETIME2(7) NOT NULL,
        [ResolvedAt] DATETIME2(7) NULL
    );
    CREATE INDEX [IX_logease_OpsIncidents_Status]
        ON [dbo].[logease_OpsIncidents]([Status]);
    CREATE INDEX [IX_logease_OpsIncidents_CreatedAt]
        ON [dbo].[logease_OpsIncidents]([CreatedAt]);
END
GO

IF OBJECT_ID(N'dbo.logease_TechDebtItems', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[logease_TechDebtItems] (
        [Id] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [PK_logease_TechDebtItems] PRIMARY KEY,
        [Title] NVARCHAR(200) NOT NULL,
        [Description] NVARCHAR(2000) NOT NULL,
        [Area] NVARCHAR(40) NOT NULL,
        [Priority] NVARCHAR(20) NOT NULL,
        [Status] NVARCHAR(30) NOT NULL,
        [CreatedAt] DATETIME2(7) NOT NULL,
        [UpdatedAt] DATETIME2(7) NOT NULL
    );
    CREATE INDEX [IX_logease_TechDebtItems_Status]
        ON [dbo].[logease_TechDebtItems]([Status]);
END
GO
