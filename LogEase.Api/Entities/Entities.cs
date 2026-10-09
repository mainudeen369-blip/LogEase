namespace LogEase.Api.Entities;

public class User
{
    public Guid Id { get; set; }
    public string Email { get; set; } = "";
    public string Name { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    /// <summary>Azure AD object id (oid) for SSO users.</summary>
    public string? AzureObjectId { get; set; }
    /// <summary>Profile photo as http(s) URL or data:image/...;base64,... from Microsoft Graph.</summary>
    public string? ProfilePhotoUrl { get; set; }
    public TimeOnly ReminderTime { get; set; } = new(17, 45);
    public bool ReminderEnabled { get; set; } = true;
    public string TimeZoneId { get; set; } = "India Standard Time";
    public string? KimaiApiToken { get; set; }
    /// <summary>
    /// Extra wording for the LLM: how this user/team writes free text so LogEase can map it to rows.
    /// </summary>
    public string? AiParseInstructions { get; set; }

    /// <summary>GitLab personal access token (read_api / read_user).</summary>
    public string? GitlabPersonalAccessToken { get; set; }
    public string? GitlabBaseUrl { get; set; }
    public string? GitlabUsername { get; set; }

    public string? MailImapHost { get; set; }
    public int? MailImapPort { get; set; }
    public string? MailImapUser { get; set; }
    public string? MailImapPassword { get; set; }
    public bool MailImapUseSsl { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<Worklog> Worklogs { get; set; } = new List<Worklog>();
    public ICollection<StickyNote> StickyNotes { get; set; } = new List<StickyNote>();
}

public class StickyNote
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public User? User { get; set; }
    /// <summary>Day the note is about (not necessarily created-at).</summary>
    public DateOnly NoteDate { get; set; }
    public string Body { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class AppSetting
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class Project
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public ICollection<Activity> Activities { get; set; } = new List<Activity>();
}

public class Activity
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public Project? Project { get; set; }
    public string Name { get; set; } = "";
}

public class Worklog
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public Guid ProjectId { get; set; }
    public Project? Project { get; set; }
    public Guid ActivityId { get; set; }
    public Activity? Activity { get; set; }
    public DateOnly WorkDate { get; set; }
    public decimal Hours { get; set; }
    public string Description { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>One AI parse attempt — observability breadcrumb.</summary>
public class AiParseEvent
{
    public Guid Id { get; set; }
    public Guid? UserId { get; set; }
    public string Parser { get; set; } = "";
    public int RowCount { get; set; }
    public int DurationMs { get; set; }
    public bool UsedFallback { get; set; }
    public bool Success { get; set; } = true;
    public string? Warning { get; set; }
    public string Source { get; set; } = "parse"; // parse | draft-sources
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Ops incident — detect → triage → mitigate → resolve.</summary>
public class OpsIncident
{
    public Guid Id { get; set; }
    public string Title { get; set; } = "";
    public string Detail { get; set; } = "";
    public string Severity { get; set; } = "Medium"; // Low | Medium | High
    public string Status { get; set; } = "Open"; // Open | Investigating | Mitigated | Resolved
    public string Category { get; set; } = "AI"; // AI | Kimai | MCP | Other
    public Guid? RelatedParseEventId { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ResolvedAt { get; set; }
}

/// <summary>Known shortcut / debt item tracked for the team.</summary>
public class TechDebtItem
{
    public Guid Id { get; set; }
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Area { get; set; } = "AI"; // AI | API | UI | Data | Ops
    public string Priority { get; set; } = "Medium"; // Low | Medium | High
    public string Status { get; set; } = "Open"; // Open | InProgress | Done
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
