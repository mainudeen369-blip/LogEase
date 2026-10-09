-- LogEase: quick SELECT * for all logease_* tables
-- Run in SSMS / Azure Data Studio against your LogEase database.
-- WARNING: logease_Users contains secrets (Kimai, GitLab, mail passwords) — use the safe query below in shared demos.

/* ========== All tables (SELECT *) ========== */

SELECT * FROM dbo.logease_Users;
SELECT * FROM dbo.logease_StickyNotes;
SELECT * FROM dbo.logease_AppSettings;
SELECT * FROM dbo.logease_Projects;
SELECT * FROM dbo.logease_Activities;
SELECT * FROM dbo.logease_Worklogs;

/* ========== Safe user check (no raw tokens/passwords) ========== */

SELECT
    Id,
    Email,
    Name,
    ReminderEnabled,
    ReminderTime,
    TimeZoneId,
    CASE WHEN KimaiApiToken IS NULL OR KimaiApiToken = N'' THEN 0 ELSE 1 END AS HasKimaiToken,
    CASE WHEN GitlabPersonalAccessToken IS NULL OR GitlabPersonalAccessToken = N'' THEN 0 ELSE 1 END AS HasGitlabToken,
    GitlabBaseUrl,
    GitlabUsername,
    MailImapHost,
    MailImapPort,
    MailImapUser,
    CASE WHEN MailImapPassword IS NULL OR MailImapPassword = N'' THEN 0 ELSE 1 END AS HasMailPassword,
    MailImapUseSsl,
    LEN(AiParseInstructions) AS AiParseInstructionsLen,
    CreatedAt
FROM dbo.logease_Users;

/* ========== GitLab token saved? (one user by email) ========== */

SELECT
    Email,
    CASE WHEN GitlabPersonalAccessToken IS NULL OR GitlabPersonalAccessToken = N'' THEN 'NOT SET' ELSE 'SET' END AS GitlabTokenStatus,
    GitlabBaseUrl,
    GitlabUsername
FROM dbo.logease_Users
WHERE Email = N'demo@logease.local';  -- change to your email

/* ========== Sticky notes for a user ========== */

SELECT n.*
FROM dbo.logease_StickyNotes n
INNER JOIN dbo.logease_Users u ON u.Id = n.UserId
WHERE u.Email = N'demo@logease.local'
ORDER BY n.NoteDate DESC;

/* ========== Worklogs for a user ========== */

SELECT w.*
FROM dbo.logease_Worklogs w
INNER JOIN dbo.logease_Users u ON u.Id = w.UserId
WHERE u.Email = N'demo@logease.local'
ORDER BY w.WorkDate DESC;
