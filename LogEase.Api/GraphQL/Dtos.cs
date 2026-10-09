namespace LogEase.Api.GraphQL;

public record LoginInput(string Email, string Password);

/// <param name="IdToken">Azure AD ID token (required for identity).</param>
/// <param name="AccessToken">Microsoft Graph access token (User.Read) — used to load profile photo.</param>
public record AzureLoginInput(string IdToken, string? AccessToken = null);

public record LoginPayload(
    bool Success,
    string? Token,
    string? Error,
    Guid? UserId,
    string? Name,
    string? Email,
    string? ProfilePhotoUrl = null,
    bool HasKimaiToken = false);

public record AuthConfigDto(
    bool AzureEnabled,
    string? TenantId,
    string? ClientId,
    string Instance,
    IReadOnlyList<string> Scopes);

public record ParsedRowDto(
    string WorkDate,
    string ProjectName,
    string ActivityName,
    decimal Hours,
    string Description,
    string Confidence,
    Guid? ProjectId,
    Guid? ActivityId);

public record ParseWorklogInput(string Text, string DateFrom, string DateTo);

public record ParseWorklogPayload(IReadOnlyList<ParsedRowDto> Rows, string Parser, string? Warning = null);

public record SaveWorklogItemInput(
    string WorkDate,
    Guid ProjectId,
    Guid ActivityId,
    decimal Hours,
    string Description);

public record SaveWorklogsInput(IReadOnlyList<SaveWorklogItemInput> Items);

public record SaveWorklogsPayload(int SavedCount, string Destination, string? Message = null);

public record UpdateReminderInput(bool Enabled, string ReminderTime);

public record ReminderStatusDto(bool ShouldRemind, string Message, string ReminderTime, bool Enabled);

public record UserSettingsDto(
    Guid Id,
    string Email,
    string Name,
    bool ReminderEnabled,
    string ReminderTime,
    string TimeZoneId,
    bool HasKimaiToken,
    KimaiUserDto? KimaiUser = null,
    string? KimaiError = null,
    string? AiParseInstructions = null,
    string? ProfilePhotoUrl = null,
    bool HasGitlabToken = false,
    string? GitlabBaseUrl = null,
    string? GitlabUsername = null,
    string? GitlabConnectedUser = null,
    string? GitlabError = null,
    bool HasMailSettings = false,
    string? MailImapHost = null,
    int? MailImapPort = null,
    string? MailImapUser = null,
    bool MailImapUseSsl = true);

public record UpdateGitlabSettingsInput(
    string? PersonalAccessToken,
    string? BaseUrl,
    string? Username);

public record UpdateMailSettingsInput(
    string? ImapHost,
    int? ImapPort,
    string? ImapUser,
    string? ImapPassword,
    bool? UseSsl);

public record StickyNoteDto(
    Guid Id,
    string NoteDate,
    string Body,
    string CreatedAt,
    string UpdatedAt);

public record CreateStickyNoteInput(string NoteDate, string Body);

public record UpdateStickyNoteInput(Guid Id, string? NoteDate, string? Body);

public record DraftFromSourcesInput(
    string DateFrom,
    string DateTo,
    bool IncludeNotes = true,
    bool IncludeGitlab = false,
    bool IncludeMail = false);

public record DraftFromSourcesPayload(
    IReadOnlyList<ParsedRowDto> Rows,
    string Parser,
    string? Warning,
    string SourcesUsed,
    string? CombinedPreview);

public record KimaiUserDto(
    int Id,
    string Username,
    string? Alias,
    string? Email,
    string? Title,
    string DisplayName);

public record UpdateKimaiSettingsInput(string? ApiToken);

public record UpdateAiParseInstructionsInput(string? Instructions);

public record AppSettingsDto(
    string WorklogSaveTarget,
    string WorklogSaveTargetLabel,
    bool KimaiBaseUrlConfigured,
    string? KimaiBaseUrl);

public record UpdateAppSettingsInput(string WorklogSaveTarget);

public record ProjectDto(Guid Id, string Name, IReadOnlyList<ActivityDto> Activities, bool IsActive = true);

public record ActivityDto(Guid Id, string Name, Guid ProjectId);

public record CreateProjectInput(string Name);

public record SetProjectActiveInput(Guid ProjectId, bool IsActive);

public record WorklogDto(
    string Id,
    string WorkDate,
    string ProjectName,
    Guid ProjectId,
    string ActivityName,
    Guid ActivityId,
    decimal Hours,
    string Description,
    string CreatedAt);

public record AiParseEventDto(
    Guid Id,
    string Parser,
    int RowCount,
    int DurationMs,
    bool UsedFallback,
    bool Success,
    string? Warning,
    string Source,
    string CreatedAt);

public record ObservabilitySummaryDto(
    int TotalParses,
    int FallbackCount,
    int SuccessCount,
    double FallbackRatePct,
    double AvgDurationMs,
    IReadOnlyList<ProviderCountDto> ByParser,
    IReadOnlyList<AiParseEventDto> Recent);

public record ProviderCountDto(string Parser, int Count);

public record OpsIncidentDto(
    Guid Id,
    string Title,
    string Detail,
    string Severity,
    string Status,
    string Category,
    Guid? RelatedParseEventId,
    string CreatedAt,
    string UpdatedAt,
    string? ResolvedAt);

public record CreateOpsIncidentInput(
    string Title,
    string Detail,
    string Severity,
    string Category);

public record UpdateOpsIncidentInput(
    Guid Id,
    string? Status,
    string? Detail,
    string? Severity);

public record TechDebtItemDto(
    Guid Id,
    string Title,
    string Description,
    string Area,
    string Priority,
    string Status,
    string CreatedAt,
    string UpdatedAt);

public record CreateTechDebtInput(
    string Title,
    string Description,
    string Area,
    string Priority);

public record UpdateTechDebtInput(
    Guid Id,
    string? Status,
    string? Priority,
    string? Description);

public record AutoTestCaseDto(
    string Id,
    string Name,
    string Category,
    string Surface,
    string Screen,
    string Operation,
    bool Passed,
    string Description,
    string? Detail,
    int DurationMs);

public record AutoTestScreenSummaryDto(
    string Screen,
    string Surface,
    bool Passed,
    int TotalTests,
    int PassedTests,
    string? Detail);

public record AutoTestReportDto(
    string RunId,
    string StartedAt,
    string CompletedAt,
    int Total,
    int Passed,
    int Failed,
    int Skipped,
    bool ApiOnline,
    string? ApiHealth,
    int DurationMs,
    IReadOnlyList<AutoTestCaseDto> Tests,
    IReadOnlyList<AutoTestScreenSummaryDto> WebScreens,
    IReadOnlyList<AutoTestScreenSummaryDto> WindowsScreens);
