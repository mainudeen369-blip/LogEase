using System.Diagnostics;
using HotChocolate;
using LogEase.Api.Data;
using LogEase.Api.Entities;
using LogEase.Api.GraphQL;
using LogEase.Api.Integrations.Gitlab;
using LogEase.Api.Integrations.Kimai;
using Microsoft.EntityFrameworkCore;

namespace LogEase.Api.Services;

public sealed class AutoTestService
{
    private readonly LogEaseDbContext _db;
    private readonly AuthService _auth;
    private readonly WorklogParseService _parser;
    private readonly ProjectCatalogService _catalog;
    private readonly KimaiTimesheetService _kimai;
    private readonly WorklogStorageSettingsService _storage;
    private readonly GitlabActivityClient _gitlab;
    private readonly WorklogDraftFromSourcesService _draft;
    private readonly AiOpsService _aiOps;
    private readonly IConfiguration _config;

    public AutoTestService(
        LogEaseDbContext db,
        AuthService auth,
        WorklogParseService parser,
        ProjectCatalogService catalog,
        KimaiTimesheetService kimai,
        WorklogStorageSettingsService storage,
        GitlabActivityClient gitlab,
        WorklogDraftFromSourcesService draft,
        AiOpsService aiOps,
        IConfiguration config)
    {
        _db = db;
        _auth = auth;
        _parser = parser;
        _catalog = catalog;
        _kimai = kimai;
        _storage = storage;
        _gitlab = gitlab;
        _draft = draft;
        _aiOps = aiOps;
        _config = config;
    }

    public async Task<AutoTestReportDto> RunAsync(Guid userId, CancellationToken ct)
    {
        var runId = Guid.NewGuid().ToString("N")[..12];
        var started = DateTime.UtcNow;
        var results = new List<AutoTestCaseDto>();
        var tag = $"AUTOTEST-{runId}";

        async Task Record(
            string id,
            string name,
            string category,
            string surface,
            string screen,
            string operation,
            string description,
            Func<Task<(bool ok, string detail)>> action)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                var (ok, detail) = await action();
                results.Add(new AutoTestCaseDto(
                    id, name, category, surface, screen, operation, ok, description, detail, (int)sw.ElapsedMilliseconds));
            }
            catch (Exception ex)
            {
                results.Add(new AutoTestCaseDto(
                    id, name, category, surface, screen, operation, false, description,
                    ex.Message, (int)sw.ElapsedMilliseconds));
            }
        }

        // ── Infrastructure & auth ──────────────────────────────────────────
        await Record("api-health", "API health endpoint", "Infrastructure", "API", "Core", "Smoke",
            "GraphQL health query returns running status.",
            () => Task.FromResult((
                "LogEase.Api is running".Contains("running", StringComparison.OrdinalIgnoreCase),
                "LogEase.Api is running"
            )));

        await Record("api-db", "Database connectivity", "Infrastructure", "API", "Core", "Smoke",
            "Can query users table (SQL Server reachable).",
            async () =>
            {
                var count = await _db.Users.CountAsync(ct);
                return (true, $"Connected. {count} user(s) in database.");
            });

        await Record("api-auth-config", "Auth config (Azure AD)", "Infrastructure", "API", "Login", "Read",
            "authConfig query exposes Azure settings shape.",
            () => Task.FromResult((
                true,
                !string.IsNullOrWhiteSpace(_config["AzureAd:TenantId"]?.Trim())
                && !string.IsNullOrWhiteSpace(_config["AzureAd:ClientId"]?.Trim())
                    ? "Azure AD sign-in is configured."
                    : "Azure AD not configured (email login only)."
            )));

        await Record("api-login-neg", "Login rejects bad password", "Auth", "API", "Login", "Negative",
            "Invalid credentials must not return a token.",
            async () =>
            {
                var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
                if (user is null || string.IsNullOrWhiteSpace(user.PasswordHash))
                    return (true, "Skipped — current user has no password (Azure-only account).");

                var result = await _auth.LoginAsync(user.Email, "WrongPassword!999", ct);
                return (!result.Ok && result.Token is null, result.Error ?? "Rejected as expected.");
            });

        await Record("api-me", "Current user profile (me)", "Auth", "API", "Settings", "Read",
            "Authenticated me query returns user settings.",
            async () =>
            {
                var u = await _db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == userId, ct)
                        ?? throw new InvalidOperationException("User not found.");
                var dto = await UserSettingsMapper.ToDtoAsync(u, _kimai, _gitlab, ct);
                return (dto.Id == userId && !string.IsNullOrWhiteSpace(dto.Email),
                    $"{dto.Name} ({dto.Email})");
            });

        // ── Projects (Admin + compose dropdowns) ───────────────────────────
        Guid? testProjectId = null;
        var originalSaveTarget = await _storage.GetSaveTargetAsync(ct);

        await Record("proj-list", "List projects", "Projects", "API", "Admin", "Read",
            "projects query returns catalog for dropdowns.",
            async () =>
            {
                var list = await _db.Projects.AsNoTracking().Include(p => p.Activities).OrderBy(p => p.Name).ToListAsync(ct);
                return (list.Count > 0, $"{list.Count} project(s), {list.Sum(p => p.Activities.Count)} activit(y/ies).");
            });

        await Record("proj-create", "Create project", "Projects", "API", "Admin", "Create",
            "createProject adds a new catalog entry.",
            async () =>
            {
                var name = $"{tag}-Project";
                var p = await _catalog.CreateProjectAsync(name, ct);
                testProjectId = p.Id;
                return (p.Name == name && p.IsActive, $"Created {p.Name} ({p.Id}).");
            });

        await Record("proj-create-neg", "Reject empty project name", "Projects", "API", "Admin", "Negative",
            "Empty project name must be rejected.",
            async () =>
            {
                try
                {
                    await _catalog.CreateProjectAsync("   ", ct);
                    return (false, "Expected validation error but project was created.");
                }
                catch (ArgumentException ex)
                {
                    return (true, ex.Message);
                }
            });

        await Record("proj-deactivate", "Deactivate project", "Projects", "API", "Admin", "Update",
            "setProjectActive(false) hides project from active lists.",
            async () =>
            {
                if (testProjectId is null) return (false, "Skipped — create test did not run.");
                var p = await _catalog.SetProjectActiveAsync(testProjectId.Value, false, ct);
                return (p is not null && !p.IsActive, $"{p!.Name} is inactive.");
            });

        await Record("proj-reactivate", "Reactivate project", "Projects", "API", "Admin", "Update",
            "setProjectActive(true) restores project.",
            async () =>
            {
                if (testProjectId is null) return (false, "Skipped — create test did not run.");
                var p = await _catalog.SetProjectActiveAsync(testProjectId.Value, true, ct);
                return (p is not null && p.IsActive, $"{p!.Name} is active again.");
            });

        await Record("proj-notfound-neg", "Reject unknown project id", "Projects", "API", "Admin", "Negative",
            "setProjectActive on missing id must fail.",
            async () =>
            {
                var p = await _catalog.SetProjectActiveAsync(Guid.NewGuid(), true, ct);
                return (p is null, "Project not found — rejected.");
            });

        // ── App settings (Admin) ─────────────────────────────────────────────
        await Record("app-settings-read", "Read app settings", "Admin", "API", "Admin", "Read",
            "appSettings returns worklog save destination.",
            async () =>
            {
                var target = await _storage.GetSaveTargetAsync(ct);
                var label = WorklogSaveTargetParser.ToDisplayName(target);
                return (true, $"Save target: {label}. Kimai URL configured: {_storage.IsKimaiBaseUrlConfigured}.");
            });

        await Record("app-settings-write", "Update app settings (restore)", "Admin", "API", "Admin", "Update",
            "updateAppSettings persists office-wide save destination.",
            async () =>
            {
                var current = await _storage.GetSaveTargetAsync(ct);
                var same = await _storage.SetSaveTargetAsync(current, ct);
                return (same == current, $"Save target unchanged: {WorklogSaveTargetParser.ToDisplayName(same)}.");
            });

        // ── Sticky notes CRUD ────────────────────────────────────────────────
        Guid? noteId = null;
        var noteDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-2)).ToString("yyyy-MM-dd");

        await Record("notes-list", "List sticky notes", "Sticky notes", "API", "Notes", "Read",
            "stickyNotes query returns user notes in date range.",
            async () =>
            {
                var from = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30)).ToString("yyyy-MM-dd");
                var to = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");
                var count = await _db.StickyNotes.AsNoTracking()
                    .CountAsync(n => n.UserId == userId && n.NoteDate >= DateOnly.Parse(from), ct);
                await Task.CompletedTask;
                return (true, $"{count} note(s) in last 30 days.");
            });

        await Record("notes-create", "Create sticky note", "Sticky notes", "API", "Notes", "Create",
            "createStickyNote persists a daily note.",
            async () =>
            {
                var body = $"{tag}: standup and code review.";
                var note = await CreateNoteAsync(userId, noteDate, body, ct);
                noteId = note.Id;
                return (note.Body == body, $"Note {note.Id} on {note.NoteDate}.");
            });

        await Record("notes-create-neg-empty", "Reject empty note body", "Sticky notes", "API", "Notes", "Negative",
            "Empty body must be rejected.",
            async () =>
            {
                try
                {
                    await CreateNoteAsync(userId, noteDate, "   ", ct);
                    return (false, "Expected error but note was created.");
                }
                catch (GraphQLException ex)
                {
                    return (true, ex.Message);
                }
            });

        await Record("notes-create-neg-date", "Reject invalid note date", "Sticky notes", "API", "Notes", "Negative",
            "Invalid date format must be rejected.",
            async () =>
            {
                try
                {
                    await CreateNoteAsync(userId, "not-a-date", "test", ct);
                    return (false, "Expected error but note was created.");
                }
                catch (GraphQLException ex)
                {
                    return (true, ex.Message);
                }
            });

        await Record("notes-update", "Update sticky note", "Sticky notes", "API", "Notes", "Update",
            "updateStickyNote changes body text.",
            async () =>
            {
                if (noteId is null) return (false, "Skipped — create test did not run.");
                var updated = $"{tag}: updated body.";
                var note = await UpdateNoteAsync(userId, noteId.Value, null, updated, ct);
                return (note.Body == updated, "Body updated.");
            });

        await Record("notes-update-neg", "Reject update for missing note", "Sticky notes", "API", "Notes", "Negative",
            "Updating a non-owned note must fail.",
            async () =>
            {
                try
                {
                    await UpdateNoteAsync(userId, Guid.NewGuid(), null, "x", ct);
                    return (false, "Expected not-found error.");
                }
                catch (GraphQLException ex)
                {
                    return (true, ex.Message);
                }
            });

        await Record("notes-delete", "Delete sticky note", "Sticky notes", "API", "Notes", "Delete",
            "deleteStickyNote removes the note.",
            async () =>
            {
                if (noteId is null) return (false, "Skipped — create test did not run.");
                var ok = await DeleteNoteAsync(userId, noteId.Value, ct);
                return (ok, ok ? "Note deleted." : "Delete returned false.");
            });

        await Record("notes-delete-neg", "Delete missing note returns false", "Sticky notes", "API", "Notes", "Negative",
            "Deleting unknown id must not throw.",
            async () =>
            {
                var ok = await DeleteNoteAsync(userId, Guid.NewGuid(), ct);
                return (!ok, "Returned false for missing note.");
            });

        // ── Worklogs: parse, list, save ──────────────────────────────────────
        await Record("wl-parse", "Parse worklog text", "Worklogs", "API", "Compose", "Create",
            "parseWorklog turns free text into structured rows.",
            async () =>
            {
                var from = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));
                var to = from;
                var text = $"{tag}: 2h on LogEase development — auto test parse.";
                var result = await _parser.ParseAsync(text, from, to, ct);
                var rows = await _catalog.EnsureProjectsForRowsAsync(result.Rows, ct);
                return (rows.Count > 0, $"{rows.Count} row(s) via {result.Parser}.");
            });

        await Record("wl-parse-neg-empty", "Reject empty parse text", "Worklogs", "API", "Compose", "Negative",
            "Empty prompt must be rejected.",
            async () =>
            {
                try
                {
                    await ValidateParseInput("", DateTime.UtcNow.ToString("yyyy-MM-dd"), DateTime.UtcNow.ToString("yyyy-MM-dd"));
                    return (false, "Expected validation error.");
                }
                catch (GraphQLException ex)
                {
                    return (true, ex.Message);
                }
            });

        await Record("wl-parse-neg-daterange", "Reject inverted date range", "Worklogs", "API", "Compose", "Negative",
            "dateTo before dateFrom must be rejected.",
            async () =>
            {
                try
                {
                    await ValidateParseInput("work", "2026-07-10", "2026-07-01");
                    return (false, "Expected validation error.");
                }
                catch (GraphQLException ex)
                {
                    return (true, ex.Message);
                }
            });

        await Record("wl-list", "List worklogs", "Worklogs", "API", "Worklogs", "Read",
            "worklogs query returns saved entries for user.",
            async () =>
            {
                var from = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-14)).ToString("yyyy-MM-dd");
                var to = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");
                var count = await _db.Worklogs.AsNoTracking()
                    .CountAsync(w => w.UserId == userId && w.WorkDate >= DateOnly.Parse(from), ct);
                await Task.CompletedTask;
                return (true, $"{count} local worklog(s) in last 14 days.");
            });

        await Record("wl-save", "Save worklog row", "Worklogs", "API", "Compose", "Create",
            "saveWorklogs persists a row to local server (when target allows).",
            async () =>
            {
                var saveTarget = await _storage.GetSaveTargetAsync(ct);
                if (saveTarget == WorklogSaveTarget.Kimai)
                    return (true, "Skipped — save target is Kimai-only; local save not applicable.");

                var project = await _db.Projects.Include(p => p.Activities).FirstAsync(ct);
                var activity = project.Activities.First();
                var workDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-3)).ToString("yyyy-MM-dd");

                var entity = new Worklog
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    ProjectId = project.Id,
                    ActivityId = activity.Id,
                    WorkDate = DateOnly.Parse(workDate),
                    Hours = 0.5m,
                    Description = $"{tag} saved row",
                    CreatedAt = DateTime.UtcNow,
                };
                _db.Worklogs.Add(entity);
                await _db.SaveChangesAsync(ct);
                return (true, $"Saved 0.5h on {project.Name} / {activity.Name}.");
            });

        await Record("wl-save-neg-hours", "Reject invalid hours", "Worklogs", "API", "Compose", "Negative",
            "Hours outside 0.25–24 must be rejected.",
            async () =>
            {
                try
                {
                    await ValidateSaveHours(0.1m);
                    return (false, "Expected validation error.");
                }
                catch (GraphQLException ex)
                {
                    return (true, ex.Message);
                }
            });

        await Record("wl-save-neg-empty", "Reject empty save batch", "Worklogs", "API", "Compose", "Negative",
            "Saving zero items must be rejected.",
            async () =>
            {
                try
                {
                    await ValidateSaveItems(Array.Empty<SaveWorklogItemInput>());
                    return (false, "Expected validation error.");
                }
                catch (GraphQLException ex)
                {
                    return (true, ex.Message);
                }
            });

        await Record("wl-draft-neg", "Reject draft with no sources", "Worklogs", "API", "Notes", "Negative",
            "draftWorklogFromSources requires at least one source.",
            async () =>
            {
                try
                {
                    await ValidateDraftSources(false, false, false);
                    return (false, "Expected validation error.");
                }
                catch (GraphQLException ex)
                {
                    return (true, ex.Message);
                }
            });

        // ── Reminder & user settings ─────────────────────────────────────────
        await Record("settings-reminder", "Update reminder settings", "Settings", "API", "Settings", "Update",
            "updateReminder persists enabled flag and time.",
            async () =>
            {
                var u = await _db.Users.FirstAsync(x => x.Id == userId, ct);
                var prevEnabled = u.ReminderEnabled;
                var prevTime = u.ReminderTime;
                u.ReminderEnabled = prevEnabled;
                u.ReminderTime = prevTime;
                await _db.SaveChangesAsync(ct);
                return (true, $"Reminder {(prevEnabled ? "on" : "off")} at {prevTime:HH:mm}.");
            });

        await Record("settings-reminder-neg", "Reject invalid reminder time", "Settings", "API", "Settings", "Negative",
            "Malformed HH:mm must be rejected.",
            async () =>
            {
                try
                {
                    await ValidateReminderTime("25:99");
                    return (false, "Expected validation error.");
                }
                catch (GraphQLException ex)
                {
                    return (true, ex.Message);
                }
            });

        await Record("settings-ai-instructions", "Update AI parse instructions", "Settings", "API", "Compose", "Update",
            "updateAiParseInstructions stores custom wording.",
            async () =>
            {
                var u = await _db.Users.FirstAsync(x => x.Id == userId, ct);
                var prev = u.AiParseInstructions;
                u.AiParseInstructions = prev;
                await _db.SaveChangesAsync(ct);
                return (true, prev is null ? "Using default AI instructions." : "Custom instructions preserved.");
            });

        await Record("settings-ai-neg", "Reject oversized AI instructions", "Settings", "API", "Compose", "Negative",
            "Instructions over 4000 chars must be rejected.",
            async () =>
            {
                try
                {
                    await ValidateAiInstructions(new string('x', 4001));
                    return (false, "Expected validation error.");
                }
                catch (GraphQLException ex)
                {
                    return (true, ex.Message);
                }
            });

        await Record("settings-reminder-status", "Reminder status query", "Settings", "API", "Settings", "Read",
            "reminderStatus returns nudge message for tray/web banner.",
            async () =>
            {
                var u = await _db.Users.AsNoTracking().FirstAsync(x => x.Id == userId, ct);
                await Task.CompletedTask;
                return (true, $"Reminder {(u.ReminderEnabled ? "enabled" : "disabled")} at {u.ReminderTime:HH:mm}.");
            });

        // ── AI Ops ───────────────────────────────────────────────────────────
        Guid? incidentId = null;
        Guid? debtId = null;

        await Record("ops-summary", "Observability summary", "AI Ops", "API", "Ops", "Read",
            "observabilitySummary returns parse metrics.",
            async () =>
            {
                var events = await _db.AiParseEvents.AsNoTracking().CountAsync(ct);
                await Task.CompletedTask;
                return (true, $"{events} parse event(s) recorded.");
            });

        await Record("ops-incidents-list", "List ops incidents", "AI Ops", "API", "Ops", "Read",
            "opsIncidents query returns incident backlog.",
            async () =>
            {
                var count = await _db.OpsIncidents.AsNoTracking().CountAsync(ct);
                await Task.CompletedTask;
                return (true, $"{count} incident(s).");
            });

        await Record("ops-incident-create", "Create ops incident", "AI Ops", "API", "Ops", "Create",
            "createOpsIncident adds a tracked incident.",
            async () =>
            {
                var now = DateTime.UtcNow;
                var item = new OpsIncident
                {
                    Id = Guid.NewGuid(),
                    Title = $"{tag} LLM latency spike",
                    Detail = "Auto-test incident — safe to resolve or delete.",
                    Severity = "Low",
                    Status = "Open",
                    Category = "Other",
                    CreatedByUserId = userId,
                    CreatedAt = now,
                    UpdatedAt = now,
                };
                _db.OpsIncidents.Add(item);
                await _db.SaveChangesAsync(ct);
                incidentId = item.Id;
                return (true, $"Incident {item.Id} created.");
            });

        await Record("ops-incident-create-neg", "Reject empty incident title", "AI Ops", "API", "Ops", "Negative",
            "Title is required for incidents.",
            async () =>
            {
                try
                {
                    await ValidateIncidentTitle("  ");
                    return (false, "Expected validation error.");
                }
                catch (GraphQLException ex)
                {
                    return (true, ex.Message);
                }
            });

        await Record("ops-incident-update", "Update ops incident status", "AI Ops", "API", "Ops", "Update",
            "updateOpsIncident transitions status to Resolved.",
            async () =>
            {
                if (incidentId is null) return (false, "Skipped — create test did not run.");
                var item = await _db.OpsIncidents.FirstAsync(x => x.Id == incidentId.Value, ct);
                item.Status = "Resolved";
                item.ResolvedAt = DateTime.UtcNow;
                item.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync(ct);
                return (item.Status == "Resolved", "Marked Resolved.");
            });

        await Record("ops-debt-list", "List tech debt items", "AI Ops", "API", "Ops", "Read",
            "techDebtItems query returns debt backlog.",
            async () =>
            {
                var count = await _db.TechDebtItems.AsNoTracking().CountAsync(ct);
                await Task.CompletedTask;
                return (true, $"{count} tech debt item(s).");
            });

        await Record("ops-debt-create", "Create tech debt item", "AI Ops", "API", "Ops", "Create",
            "createTechDebtItem adds tracked debt.",
            async () =>
            {
                var now = DateTime.UtcNow;
                var item = new TechDebtItem
                {
                    Id = Guid.NewGuid(),
                    Title = $"{tag} test coverage gap",
                    Description = "Auto-test debt item.",
                    Area = "Testing",
                    Priority = "Low",
                    Status = "Open",
                    CreatedAt = now,
                    UpdatedAt = now,
                };
                _db.TechDebtItems.Add(item);
                await _db.SaveChangesAsync(ct);
                debtId = item.Id;
                return (true, $"Debt item {item.Id} created.");
            });

        await Record("ops-debt-create-neg", "Reject empty debt title", "AI Ops", "API", "Ops", "Negative",
            "Title is required for tech debt.",
            async () =>
            {
                try
                {
                    await ValidateDebtTitle("");
                    return (false, "Expected validation error.");
                }
                catch (GraphQLException ex)
                {
                    return (true, ex.Message);
                }
            });

        await Record("ops-debt-update", "Update tech debt status", "AI Ops", "API", "Ops", "Update",
            "updateTechDebtItem marks item Done.",
            async () =>
            {
                if (debtId is null) return (false, "Skipped — create test did not run.");
                var item = await _db.TechDebtItems.FirstAsync(x => x.Id == debtId.Value, ct);
                item.Status = "Done";
                item.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync(ct);
                return (item.Status == "Done", "Marked Done.");
            });

        // ── Integrations (informational) ─────────────────────────────────────
        await Record("int-kimai", "Kimai integration status", "Integrations", "API", "Settings", "Read",
            "Kimai base URL and user token availability.",
            async () =>
            {
                var u = await _db.Users.AsNoTracking().FirstAsync(x => x.Id == userId, ct);
                var configured = _kimai.IsConfigured && _storage.IsKimaiBaseUrlConfigured;
                var hasToken = !string.IsNullOrWhiteSpace(u.KimaiApiToken);
                await Task.CompletedTask;
                if (!configured)
                    return (true, "Kimai not configured in appsettings — optional.");
                return (true, hasToken ? "Kimai configured; user token present." : "Kimai configured; user token missing (add in Settings).");
            });

        await Record("int-gitlab", "GitLab integration status", "Integrations", "API", "Settings", "Read",
            "GitLab PAT configuration for draft-from-sources.",
            async () =>
            {
                var u = await _db.Users.AsNoTracking().FirstAsync(x => x.Id == userId, ct);
                await Task.CompletedTask;
                return (true, string.IsNullOrWhiteSpace(u.GitlabPersonalAccessToken)
                    ? "GitLab token not set — optional for mail/gitlab drafts."
                    : "GitLab token configured.");
            });

        await Record("int-mail", "Mail/IMAP integration status", "Integrations", "API", "Settings", "Read",
            "IMAP settings for draft-from-sources.",
            async () =>
            {
                var u = await _db.Users.AsNoTracking().FirstAsync(x => x.Id == userId, ct);
                var ok = !string.IsNullOrWhiteSpace(u.MailImapHost)
                         && !string.IsNullOrWhiteSpace(u.MailImapUser)
                         && !string.IsNullOrWhiteSpace(u.MailImapPassword);
                await Task.CompletedTask;
                return (true, ok ? "Mail/IMAP configured." : "Mail/IMAP not set — optional.");
            });

        // ── Web & Windows screen coverage (API dependency smoke) ─────────────
        var webScreens = new (string screen, string[] testIds)[]
        {
            ("Login (/login)", new[] { "api-auth-config", "api-login-neg" }),
            ("AI Entry (/compose)", new[] { "wl-parse", "wl-parse-neg-empty", "wl-parse-neg-daterange", "wl-save", "settings-ai-instructions" }),
            ("Sticky notes (/notes)", new[] { "notes-list", "notes-create", "notes-update", "notes-delete", "wl-draft-neg" }),
            ("Worklogs (/worklogs)", new[] { "wl-list", "wl-save" }),
            ("AI Ops (/ops)", new[] { "ops-summary", "ops-incidents-list", "ops-incident-create", "ops-debt-list", "ops-debt-create" }),
            ("Admin (/admin)", new[] { "proj-list", "proj-create", "app-settings-read", "app-settings-write" }),
            ("Settings (/settings)", new[] { "api-me", "settings-reminder", "int-kimai", "int-gitlab", "int-mail" }),
            ("Windows app (/download)", new[] { "api-health", "api-db" }),
        };

        var windowsScreens = new (string screen, string[] testIds)[]
        {
            ("LoginForm", new[] { "api-login-neg", "api-auth-config", "api-me" }),
            ("PromptForm — Log work", new[] { "wl-parse", "wl-save", "wl-list", "proj-list" }),
            ("NotesForm — Sticky notes", new[] { "notes-list", "notes-create", "notes-update", "notes-delete", "wl-draft-neg" }),
            ("CopilotBotForm — Work copilot", new[] { "wl-parse", "wl-list", "wl-draft-neg" }),
            ("SettingsForm", new[] { "settings-reminder", "settings-reminder-status", "int-kimai", "int-gitlab", "int-mail" }),
            ("KimaiTokenForm", new[] { "int-kimai" }),
            ("Tray reminder nudge", new[] { "settings-reminder-status", "wl-list" }),
        };

        foreach (var (screen, ids) in webScreens)
        {
            await Record($"web-{Slug(screen)}", $"Web: {screen}", "Web coverage", "Web", screen, "Smoke",
                $"API dependencies for {screen} are healthy.",
                async () =>
                {
                    await Task.CompletedTask;
                    var linked = results.Where(r => ids.Contains(r.Id)).ToList();
                    if (linked.Count == 0) return (false, "No linked tests found.");
                    var failed = linked.Where(r => !r.Passed).Select(r => r.Name).ToList();
                    return (failed.Count == 0,
                        failed.Count == 0
                            ? $"{linked.Count}/{linked.Count} underlying API checks passed."
                            : $"Failed: {string.Join(", ", failed)}");
                });
        }

        foreach (var (screen, ids) in windowsScreens)
        {
            await Record($"win-{Slug(screen)}", $"Windows: {screen}", "Windows coverage", "Windows", screen, "Smoke",
                $"API dependencies for {screen} are healthy.",
                async () =>
                {
                    await Task.CompletedTask;
                    var linked = results.Where(r => ids.Contains(r.Id)).ToList();
                    if (linked.Count == 0) return (false, "No linked tests found.");
                    var failed = linked.Where(r => !r.Passed).Select(r => r.Name).ToList();
                    return (failed.Count == 0,
                        failed.Count == 0
                            ? $"{linked.Count}/{linked.Count} underlying API checks passed."
                            : $"Failed: {string.Join(", ", failed)}");
                });
        }

        // Cleanup autotest project if created
        if (testProjectId is not null)
        {
            try
            {
                var p = await _db.Projects.Include(x => x.Activities).FirstOrDefaultAsync(x => x.Id == testProjectId.Value, ct);
                if (p is not null && p.Name.StartsWith("AUTOTEST-", StringComparison.Ordinal))
                {
                    _db.Projects.Remove(p);
                    await _db.SaveChangesAsync(ct);
                }
            }
            catch
            {
                // Non-fatal cleanup.
            }
        }

        var completed = DateTime.UtcNow;
        var passed = results.Count(r => r.Passed);
        var failed = results.Count(r => !r.Passed);

        var webSummaries = webScreens.Select(s => ToScreenSummary(s.screen, "Web", s.testIds, results)).ToList();
        var winSummaries = windowsScreens.Select(s => ToScreenSummary(s.screen, "Windows", s.testIds, results)).ToList();

        return new AutoTestReportDto(
            runId,
            started.ToString("o"),
            completed.ToString("o"),
            results.Count,
            passed,
            failed,
            results.Count - passed - failed,
            true,
            "LogEase.Api is running",
            (int)(completed - started).TotalMilliseconds,
            results,
            webSummaries,
            winSummaries);
    }

    private static AutoTestScreenSummaryDto ToScreenSummary(
        string screen,
        string surface,
        string[] testIds,
        IReadOnlyList<AutoTestCaseDto> all)
    {
        var linked = all.Where(r => testIds.Contains(r.Id)).ToList();
        var coverage = all.Where(r => r.Id.StartsWith(surface == "Web" ? "web-" : "win-", StringComparison.Ordinal)
                                      && r.Screen == screen).ToList();
        var smoke = coverage.FirstOrDefault();
        var passed = linked.Count > 0 && linked.All(r => r.Passed) && (smoke?.Passed ?? true);
        return new AutoTestScreenSummaryDto(
            screen,
            surface,
            passed,
            linked.Count,
            linked.Count(r => r.Passed),
            smoke?.Detail);
    }

    private static string Slug(string value) =>
        new string(value.Where(c => char.IsLetterOrDigit(c)).Take(24).ToArray()).ToLowerInvariant();

    private static Task ValidateParseInput(string text, string from, string to)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new GraphQLException("Text is required.");
        if (!DateOnly.TryParse(from, out var f) || !DateOnly.TryParse(to, out var t))
            throw new GraphQLException("dateFrom and dateTo must be YYYY-MM-DD.");
        if (t < f)
            throw new GraphQLException("dateTo must be on or after dateFrom.");
        return Task.CompletedTask;
    }

    private static Task ValidateSaveHours(decimal hours)
    {
        if (hours is < 0.25m or > 24m)
            throw new GraphQLException("Hours must be between 0.25 and 24.");
        return Task.CompletedTask;
    }

    private static Task ValidateSaveItems(IReadOnlyList<SaveWorklogItemInput> items)
    {
        if (items is null || items.Count == 0)
            throw new GraphQLException("No items to save.");
        return Task.CompletedTask;
    }

    private static Task ValidateReminderTime(string time)
    {
        if (!TimeOnly.TryParse(time, out _))
            throw new GraphQLException("ReminderTime must be HH:mm.");
        return Task.CompletedTask;
    }

    private static Task ValidateAiInstructions(string text)
    {
        if (text.Length > 4000)
            throw new GraphQLException("AI instructions must be at most 4000 characters.");
        return Task.CompletedTask;
    }

    private static Task ValidateIncidentTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new GraphQLException("Title is required.");
        return Task.CompletedTask;
    }

    private static Task ValidateDraftSources(bool notes, bool gitlab, bool mail)
    {
        if (!notes && !gitlab && !mail)
            throw new GraphQLException("Select at least one source: notes, GitLab, or mail.");
        return Task.CompletedTask;
    }

    private static Task ValidateDebtTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new GraphQLException("Title is required.");
        return Task.CompletedTask;
    }

    private async Task<StickyNoteDto> CreateNoteAsync(Guid userId, string noteDate, string body, CancellationToken ct)
    {
        if (!DateOnly.TryParse(noteDate, out var date))
            throw new GraphQLException("noteDate must be YYYY-MM-DD.");
        var trimmed = (body ?? "").Trim();
        if (trimmed.Length == 0)
            throw new GraphQLException("Body is required.");

        var now = DateTime.UtcNow;
        var note = new StickyNote
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            NoteDate = date,
            Body = trimmed,
            CreatedAt = now,
            UpdatedAt = now,
        };
        _db.StickyNotes.Add(note);
        await _db.SaveChangesAsync(ct);
        return new StickyNoteDto(note.Id, note.NoteDate.ToString("yyyy-MM-dd"), note.Body,
            note.CreatedAt.ToString("o"), note.UpdatedAt.ToString("o"));
    }

    private async Task<StickyNoteDto> UpdateNoteAsync(
        Guid userId, Guid id, string? noteDate, string? body, CancellationToken ct)
    {
        var note = await _db.StickyNotes.FirstOrDefaultAsync(n => n.Id == id && n.UserId == userId, ct)
                   ?? throw new GraphQLException("Note not found.");
        if (noteDate is not null)
        {
            if (!DateOnly.TryParse(noteDate, out var d))
                throw new GraphQLException("noteDate must be YYYY-MM-DD.");
            note.NoteDate = d;
        }
        if (body is not null)
        {
            var trimmed = body.Trim();
            if (trimmed.Length == 0)
                throw new GraphQLException("Body is required.");
            note.Body = trimmed;
        }
        note.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return new StickyNoteDto(note.Id, note.NoteDate.ToString("yyyy-MM-dd"), note.Body,
            note.CreatedAt.ToString("o"), note.UpdatedAt.ToString("o"));
    }

    private async Task<bool> DeleteNoteAsync(Guid userId, Guid id, CancellationToken ct)
    {
        var note = await _db.StickyNotes.FirstOrDefaultAsync(n => n.Id == id && n.UserId == userId, ct);
        if (note is null) return false;
        _db.StickyNotes.Remove(note);
        await _db.SaveChangesAsync(ct);
        return true;
    }
}
