-- Add per-user AI parse instructions (Customize prompt wording).
IF COL_LENGTH(N'dbo.logease_Users', N'AiParseInstructions') IS NULL
BEGIN
    ALTER TABLE [dbo].[logease_Users]
        ADD [AiParseInstructions] NVARCHAR(4000) NULL;
END
GO
