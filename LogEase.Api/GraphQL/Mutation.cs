using System.Security.Claims;
using System.Text.RegularExpressions;
using HotChocolate.Authorization;
using LogEase.Api.Data;
using LogEase.Api.Entities;
using LogEase.Api.Integrations.Gitlab;
using LogEase.Api.Integrations.Kimai;
using LogEase.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace LogEase.Api.GraphQL;

public class Mutation
{
    public async Task<LoginPayload> Login(
        LoginInput input,
        [Service] AuthService auth,
        CancellationToken ct)
    {
        var result = await auth.LoginAsync(input.Email, input.Password, ct);
        if (!result.Ok)
            return new LoginPayload(false, null, result.Error, null, null, null);

        return ToLoginPayload(result);
    }

    public async Task<LoginPayload> LoginWithAzure(
        AzureLoginInput input,
        [Service] AzureAdAuthService azureAuth,
        CancellationToken ct)
    {
        try
        {
            var result = await azureAuth.LoginWithIdTokenAsync(input.IdToken, input.AccessToken, ct);
            if (!result.Ok)
                return new LoginPayload(false, null, result.Error, null, null, null);

            return ToLoginPayload(result);
        }
        catch (Exception ex)
        {
            return new LoginPayload(
                false,
                null,
                "Azure login failed on server: " + ex.Message,
                null,
                null,
                null);
        }
    }

    private static LoginPayload ToLoginPayload(LoginResult result) =>
        new(
            true,
            result.Token,
            null,
            result.UserId,
            result.Name,
            result.Email,
            result.ProfilePhotoUrl,
            result.HasKimaiToken);

    [Authorize]
    public async Task<ParseWorklogPayload> ParseWorklog(
        ParseWorklogInput input,
        [Service] WorklogParseService parser,
        [Service] ProjectCatalogService catalog,
        [Service] LogEaseDbContext db,
        [Service] KimaiTimesheetService kimai,
        [Service] WorklogStorageSettingsService storage,
        [Service] AiOpsService aiOps,
        ClaimsPrincipal principal,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input.Text))
            throw new GraphQLException("Text is required.");

        if (!DateOnly.TryParse(input.DateFrom, out var from) || !DateOnly.TryParse(input.DateTo, out var to))
            throw new GraphQLException("dateFrom and dateTo must be YYYY-MM-DD.");

        if (to < from)
            throw new GraphQLException("dateTo must be on or after dateFrom.");

        var text = input.Text.Trim();
        var userId = Guid.Parse(
            principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? principal.FindFirstValue("sub")!);
        var user = await db.Users.AsNoTracking().FirstAsync(x => x.Id == userId, ct);
        var saveTarget = await storage.GetSaveTargetAsync(ct);
        var useKimaiCatalog = saveTarget is WorklogSaveTarget.Kimai or WorklogSaveTarget.Both
                              && !string.IsNullOrWhiteSpace(user.KimaiApiToken)
                              && kimai.IsConfigured;

        IReadOnlyList<ParsedWorklogRow> rows;
        string? warning = null;
        string parserName;
        var sw = System.Diagnostics.Stopwatch.StartNew();

        if (useKimaiCatalog)
        {
            var kimaiCatalog = await kimai.GetCatalogAsync(user.KimaiApiToken!, ct);
            var result = await parser.ParseAsync(text, from, to, ct, user.AiParseInstructions);
            rows = MapRowsToKimaiCatalog(result.Rows, kimaiCatalog);
            parserName = result.Parser;
            warning = result.Warning;
            if (rows.Any(r => r.ProjectId is null))
            {
                warning = (warning is null ? "" : warning + " ")
                          + "Some rows need a Kimai project selected from the dropdown.";
            }
        }
        else
        {
            await catalog.EnsureNamedProjectsAsync(ExtractExplicitProjectNames(text), ct);
            var result = await parser.ParseAsync(text, from, to, ct, user.AiParseInstructions);
            rows = await catalog.EnsureProjectsForRowsAsync(result.Rows, ct);
            parserName = result.Parser;
            warning = result.Warning;
        }

        sw.Stop();
        try
        {
            await aiOps.RecordParseAsync(
                userId, parserName, rows.Count, (int)sw.ElapsedMilliseconds, warning, "parse", ct);
        }
        catch
        {
            // Observability must not break the draft path.
        }

        return new ParseWorklogPayload(
            rows.Select(r => new ParsedRowDto(
                r.WorkDate.ToString("yyyy-MM-dd"),
                r.ProjectName,
                r.ActivityName,
                r.Hours,
                r.Description,
                r.Confidence,
                r.ProjectId,
                r.ActivityId)).ToList(),
            parserName,
            warning);
    }

    private static IReadOnlyList<ParsedWorklogRow> MapRowsToKimaiCatalog(
        IReadOnlyList<ParsedWorklogRow> rows,
        IReadOnlyList<(KimaiProject Project, IReadOnlyList<KimaiActivity> Activities)> catalog)
    {
        var projects = catalog.Select(c => c.Project).ToList();
        var mapped = new List<ParsedWorklogRow>();

        foreach (var row in rows)
        {
            var project = MatchKimaiProject(projects, row.ProjectName);
            if (project is null)
            {
                mapped.Add(row with { ProjectId = null, ActivityId = null });
                continue;
            }

            var activities = catalog.First(c => c.Project.Id == project.Id).Activities;
            var activity = MatchKimaiActivity(activities, project.Id, row.ActivityName)
                           ?? activities.FirstOrDefault();

            mapped.Add(row with
            {
                ProjectName = project.Name,
                ActivityName = activity?.Name ?? row.ActivityName,
                ProjectId = KimaiIdCodec.ToProjectGuid(project.Id),
                ActivityId = activity is null ? null : KimaiIdCodec.ToActivityGuid(activity.Id),
            });
        }

        return mapped;
    }

    private static KimaiProject? MatchKimaiProject(IReadOnlyList<KimaiProject> projects, string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var exact = projects.FirstOrDefault(p =>
            string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        if (exact is not null) return exact;

        return projects
            .Select(p => new { Project = p, Score = ProjectCatalogService.FuzzyScore(p.Name, name) })
            .Where(x => x.Score >= 45)
            .OrderByDescending(x => x.Score)
            .Select(x => x.Project)
            .FirstOrDefault();
    }

    private static KimaiActivity? MatchKimaiActivity(
        IReadOnlyList<KimaiActivity> activities,
        int projectId,
        string? name)
    {
        var scoped = activities.Where(a => a.ProjectId == projectId || a.ProjectId == 0).ToList();
        if (scoped.Count == 0) scoped = activities.ToList();
        if (scoped.Count == 0) return null;
        if (string.IsNullOrWhiteSpace(name)) return scoped.First();

        var exact = scoped.FirstOrDefault(a =>
            string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));
        if (exact is not null) return exact;

        return scoped
            .Select(a => new { Activity = a, Score = ProjectCatalogService.FuzzyScore(a.Name, name) })
            .Where(x => x.Score >= 40)
            .OrderByDescending(x => x.Score)
            .Select(x => x.Activity)
            .FirstOrDefault()
            ?? scoped.First();
    }

    private static IEnumerable<string> ExtractExplicitProjectNames(string text)
    {
        var names = new List<string>();
        var patterns = new[]
        {
            @"other\s+proj\w*\s+is\s+([A-Za-z][\w\s\-/&]{1,60}?)(?:\s+i\s+worked|\s+for\s+|[,.\n]|$)",
            @"proj\w*\s*:\s*([A-Za-z][\w\s\-/&]{1,60})",
            @"worked\s+on\s+([A-Za-z][\w\s\-/&]{1,40}?)\s+(?:ai|for|and|,)",
        };
        foreach (var p in patterns)
        {
            foreach (Match m in Regex.Matches(text, p, RegexOptions.IgnoreCase))
            {
                var n = m.Groups[1].Value.Trim().TrimEnd('.', ',');
                if (n.Length is >= 2 and <= 80) names.Add(n);
            }
        }
        return names.Distinct(StringComparer.OrdinalIgnoreCase);
    }

    [Authorize]
    public async Task<SaveWorklogsPayload> SaveWorklogs(
        SaveWorklogsInput input,
        [Service] LogEaseDbContext db,
        [Service] KimaiTimesheetService kimai,
        [Service] WorklogStorageSettingsService storage,
        ClaimsPrincipal principal,
        CancellationToken ct)
    {
        if (input.Items is null || input.Items.Count == 0)
            throw new GraphQLException("No items to save.");

        var userId = Guid.Parse(
            principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? principal.FindFirstValue("sub")!);

        var user = await db.Users.AsNoTracking().FirstAsync(x => x.Id == userId, ct);
        var saveTarget = await storage.GetSaveTargetAsync(ct);
        var drafts = new List<KimaiTimesheetDraft>();
        var entities = new List<Worklog>();

        // Load Kimai catalog at most once (never per row — that made Save hang for minutes).
        IReadOnlyList<(KimaiProject Project, IReadOnlyList<KimaiActivity> Activities)>? kimaiCatalog = null;
        var needsKimaiNames = input.Items.Any(i =>
            KimaiIdCodec.TryGetProjectId(i.ProjectId, out _)
            && KimaiIdCodec.TryGetActivityId(i.ActivityId, out _));
        if (needsKimaiNames
            && !string.IsNullOrWhiteSpace(user.KimaiApiToken)
            && kimai.IsConfigured
            && saveTarget is WorklogSaveTarget.Kimai or WorklogSaveTarget.Both)
        {
            // Only when posting to Kimai. LocalServer uses placeholder names — no Kimai HTTP on Save.
            kimaiCatalog = await kimai.GetCatalogAsync(user.KimaiApiToken!, ct);
        }

        foreach (var item in input.Items)
        {
            if (!DateOnly.TryParse(item.WorkDate, out var date))
                throw new GraphQLException($"Invalid date: {item.WorkDate}");
            if (item.Hours is < 0.25m or > 24m)
                throw new GraphQLException("Hours must be between 0.25 and 24.");

            string projectName;
            string activityName;
            Guid localProjectId = item.ProjectId;
            Guid localActivityId = item.ActivityId;
            int? kimaiProjectId = null;
            int? kimaiActivityId = null;

            if (KimaiIdCodec.TryGetProjectId(item.ProjectId, out var kp)
                && KimaiIdCodec.TryGetActivityId(item.ActivityId, out var ka))
            {
                kimaiProjectId = kp;
                kimaiActivityId = ka;

                if (kimaiCatalog is not null)
                {
                    var project = kimaiCatalog.Select(c => c.Project).FirstOrDefault(p => p.Id == kp)
                                  ?? throw new GraphQLException("Selected Kimai project is no longer available.");
                    var activities = kimaiCatalog.First(c => c.Project.Id == kp).Activities;
                    var activity = activities.FirstOrDefault(a => a.Id == ka)
                                   ?? throw new GraphQLException(
                                       $"Selected Kimai activity is not available on '{project.Name}'.");
                    projectName = project.Name;
                    activityName = activity.Name;
                }
                else
                {
                    projectName = $"Kimai project #{kp}";
                    activityName = $"Kimai activity #{ka}";
                }

                if (saveTarget is WorklogSaveTarget.Both or WorklogSaveTarget.LocalServer)
                {
                    var localProject = await EnsureLocalProjectForKimaiAsync(db, projectName, activityName, ct);
                    localProjectId = localProject.ProjectId;
                    localActivityId = localProject.ActivityId;
                }
            }
            else
            {
                var project = await db.Projects
                    .AsNoTracking()
                    .Include(p => p.Activities)
                    .FirstOrDefaultAsync(p => p.Id == item.ProjectId, ct);
                var activity = project?.Activities.FirstOrDefault(a => a.Id == item.ActivityId);
                if (project is null || activity is null)
                    throw new GraphQLException("Invalid project or activity.");

                projectName = project.Name;
                activityName = activity.Name;
            }

            drafts.Add(new KimaiTimesheetDraft(
                date,
                item.Hours,
                (item.Description ?? "").Trim(),
                projectName,
                activityName,
                kimaiProjectId,
                kimaiActivityId));

            entities.Add(new Worklog
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                ProjectId = localProjectId,
                ActivityId = localActivityId,
                WorkDate = date,
                Hours = item.Hours,
                Description = (item.Description ?? "").Trim(),
                CreatedAt = DateTime.UtcNow,
            });
        }

        var toKimai = saveTarget is WorklogSaveTarget.Kimai or WorklogSaveTarget.Both;
        var toLocal = saveTarget is WorklogSaveTarget.LocalServer or WorklogSaveTarget.Both;

        KimaiSyncResult? kimaiResult = null;
        if (toKimai)
        {
            if (!kimai.IsConfigured || !storage.IsKimaiBaseUrlConfigured)
                throw new GraphQLException(
                    "Save target includes Kimai, but Kimai:BaseUrl is not set in appsettings.");

            if (string.IsNullOrWhiteSpace(user.KimaiApiToken))
                throw new GraphQLException(
                    "Save target includes Kimai. Add your Kimai API token in Settings first.");

            kimaiResult = await kimai.CreateTimesheetsAsync(user.KimaiApiToken, drafts, ct);
            if (!kimaiResult.Success)
                throw new GraphQLException(kimaiResult.Message);
        }

        if (toLocal)
        {
            db.Worklogs.AddRange(entities);
            await db.SaveChangesAsync(ct);
        }

        var count = toKimai ? (kimaiResult?.CreatedCount ?? drafts.Count) : entities.Count;
        var destination = WorklogSaveTargetParser.ToConfigValue(saveTarget);
        var message = saveTarget switch
        {
            WorklogSaveTarget.Kimai =>
                $"Posted {kimaiResult!.CreatedCount} timesheet(s) to Kimai.",
            WorklogSaveTarget.Both =>
                $"Saved {entities.Count} worklog(s) to local server and posted {kimaiResult!.CreatedCount} to Kimai.",
            _ =>
                $"Saved {entities.Count} worklog(s) to local server.",
        };

        return new SaveWorklogsPayload(count, destination, message);
    }

    private static async Task<(Guid ProjectId, Guid ActivityId)> EnsureLocalProjectForKimaiAsync(
        LogEaseDbContext db,
        string projectName,
        string activityName,
        CancellationToken ct)
    {
        var project = await db.Projects
            .Include(p => p.Activities)
            .FirstOrDefaultAsync(p => p.Name == projectName, ct);

        if (project is null)
        {
            project = new Project
            {
                Id = Guid.NewGuid(),
                Name = projectName,
                IsActive = true,
                Activities =
                [
                    new Activity { Id = Guid.NewGuid(), Name = activityName },
                    new Activity { Id = Guid.NewGuid(), Name = "Development" },
                ],
            };
            foreach (var a in project.Activities)
                a.ProjectId = project.Id;
            db.Projects.Add(project);
            await db.SaveChangesAsync(ct);
        }

        var activity = project.Activities.FirstOrDefault(a =>
                           string.Equals(a.Name, activityName, StringComparison.OrdinalIgnoreCase))
                       ?? project.Activities.FirstOrDefault();
        if (activity is null)
        {
            activity = new Activity
            {
                Id = Guid.NewGuid(),
                ProjectId = project.Id,
                Name = activityName,
            };
            db.Activities.Add(activity);
            await db.SaveChangesAsync(ct);
        }

        return (project.Id, activity.Id);
    }

    [Authorize]
    public async Task<UserSettingsDto> UpdateReminder(
        UpdateReminderInput input,
        [Service] LogEaseDbContext db,
        [Service] KimaiTimesheetService kimai,
        [Service] GitlabActivityClient gitlab,
        ClaimsPrincipal principal,
        CancellationToken ct)
    {
        var userId = Guid.Parse(
            principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? principal.FindFirstValue("sub")!);

        var u = await db.Users.FirstAsync(x => x.Id == userId, ct);
        if (!TimeOnly.TryParse(input.ReminderTime, out var time))
            throw new GraphQLException("ReminderTime must be HH:mm.");

        u.ReminderEnabled = input.Enabled;
        u.ReminderTime = time;
        await db.SaveChangesAsync(ct);

        return await UserSettingsMapper.ToDtoAsync(u, kimai, gitlab, ct);
    }

    [Authorize]
    public async Task<UserSettingsDto> UpdateKimaiSettings(
        UpdateKimaiSettingsInput input,
        [Service] LogEaseDbContext db,
        [Service] KimaiTimesheetService kimai,
        [Service] GitlabActivityClient gitlab,
        ClaimsPrincipal principal,
        CancellationToken ct)
    {
        var userId = Guid.Parse(
            principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? principal.FindFirstValue("sub")!);

        var u = await db.Users.FirstAsync(x => x.Id == userId, ct);
        if (input.ApiToken is not null)
            u.KimaiApiToken = string.IsNullOrWhiteSpace(input.ApiToken) ? null : input.ApiToken.Trim();

        // Validate new token against Kimai before keeping it when possible.
        if (!string.IsNullOrWhiteSpace(u.KimaiApiToken) && kimai.IsConfigured)
        {
            try
            {
                var profile = await kimai.GetCurrentUserAsync(u.KimaiApiToken, ct);
                if (profile is null)
                    throw new GraphQLException("Kimai token did not return a user profile. Check the token.");
            }
            catch (GraphQLException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new GraphQLException("Kimai rejected this token: " + ex.Message);
            }
        }

        await db.SaveChangesAsync(ct);

        return await UserSettingsMapper.ToDtoAsync(u, kimai, gitlab, ct);
    }

    [Authorize]
    public async Task<UserSettingsDto> UpdateAiParseInstructions(
        UpdateAiParseInstructionsInput input,
        [Service] LogEaseDbContext db,
        [Service] KimaiTimesheetService kimai,
        [Service] GitlabActivityClient gitlab,
        ClaimsPrincipal principal,
        CancellationToken ct)
    {
        var userId = Guid.Parse(
            principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? principal.FindFirstValue("sub")!);

        var u = await db.Users.FirstAsync(x => x.Id == userId, ct);
        var text = input.Instructions?.Trim();
        if (string.IsNullOrWhiteSpace(text))
            u.AiParseInstructions = null;
        else if (text.Length > 4000)
            throw new GraphQLException("AI instructions must be at most 4000 characters.");
        else
            u.AiParseInstructions = text;

        await db.SaveChangesAsync(ct);
        return await UserSettingsMapper.ToDtoAsync(u, kimai, gitlab, ct);
    }

    [Authorize]
    public async Task<AppSettingsDto> UpdateAppSettings(
        UpdateAppSettingsInput input,
        [Service] WorklogStorageSettingsService storage,
        [Service] IConfiguration config,
        CancellationToken ct)
    {
        var target = WorklogSaveTargetParser.Parse(input.WorklogSaveTarget);
        if (target is WorklogSaveTarget.Kimai or WorklogSaveTarget.Both
            && !storage.IsKimaiBaseUrlConfigured)
        {
            throw new GraphQLException(
                "Set a real Kimai:BaseUrl in appsettings before choosing Kimai or Both.");
        }

        target = await storage.SetSaveTargetAsync(target, ct);
        var baseUrl = config["Kimai:BaseUrl"]?.Trim();
        return new AppSettingsDto(
            WorklogSaveTargetParser.ToConfigValue(target),
            WorklogSaveTargetParser.ToDisplayName(target),
            storage.IsKimaiBaseUrlConfigured,
            storage.IsKimaiBaseUrlConfigured ? baseUrl : null);
    }

    [Authorize]
    public async Task<ProjectDto> CreateProject(
        CreateProjectInput input,
        [Service] ProjectCatalogService catalog,
        CancellationToken ct)
    {
        try
        {
            var p = await catalog.CreateProjectAsync(input.Name, ct);
            return new ProjectDto(
                p.Id,
                p.Name,
                p.Activities.OrderBy(a => a.Name)
                    .Select(a => new ActivityDto(a.Id, a.Name, a.ProjectId))
                    .ToList(),
                p.IsActive);
        }
        catch (ArgumentException ex)
        {
            throw new GraphQLException(ex.Message);
        }
    }

    [Authorize]
    public async Task<ProjectDto> SetProjectActive(
        SetProjectActiveInput input,
        [Service] ProjectCatalogService catalog,
        CancellationToken ct)
    {
        var p = await catalog.SetProjectActiveAsync(input.ProjectId, input.IsActive, ct)
                ?? throw new GraphQLException("Project not found.");
        return new ProjectDto(
            p.Id,
            p.Name,
            p.Activities.OrderBy(a => a.Name)
                .Select(a => new ActivityDto(a.Id, a.Name, a.ProjectId))
                .ToList(),
            p.IsActive);
    }

    [Authorize]
    public async Task<StickyNoteDto> CreateStickyNote(
        CreateStickyNoteInput input,
        [Service] LogEaseDbContext db,
        ClaimsPrincipal principal,
        CancellationToken ct)
    {
        if (!DateOnly.TryParse(input.NoteDate, out var noteDate))
            throw new GraphQLException("noteDate must be YYYY-MM-DD.");
        var body = (input.Body ?? "").Trim();
        if (body.Length == 0)
            throw new GraphQLException("Body is required.");
        if (body.Length > 4000)
            throw new GraphQLException("Body must be at most 4000 characters.");

        var userId = Guid.Parse(
            principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? principal.FindFirstValue("sub")!);
        var now = DateTime.UtcNow;
        var note = new StickyNote
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            NoteDate = noteDate,
            Body = body,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.StickyNotes.Add(note);
        await db.SaveChangesAsync(ct);
        return ToNoteDto(note);
    }

    [Authorize]
    public async Task<StickyNoteDto> UpdateStickyNote(
        UpdateStickyNoteInput input,
        [Service] LogEaseDbContext db,
        ClaimsPrincipal principal,
        CancellationToken ct)
    {
        var userId = Guid.Parse(
            principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? principal.FindFirstValue("sub")!);
        var note = await db.StickyNotes.FirstOrDefaultAsync(n => n.Id == input.Id && n.UserId == userId, ct)
                   ?? throw new GraphQLException("Note not found.");

        if (input.NoteDate is not null)
        {
            if (!DateOnly.TryParse(input.NoteDate, out var noteDate))
                throw new GraphQLException("noteDate must be YYYY-MM-DD.");
            note.NoteDate = noteDate;
        }

        if (input.Body is not null)
        {
            var body = input.Body.Trim();
            if (body.Length == 0)
                throw new GraphQLException("Body is required.");
            if (body.Length > 4000)
                throw new GraphQLException("Body must be at most 4000 characters.");
            note.Body = body;
        }

        note.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return ToNoteDto(note);
    }

    [Authorize]
    public async Task<bool> DeleteStickyNote(
        Guid id,
        [Service] LogEaseDbContext db,
        ClaimsPrincipal principal,
        CancellationToken ct)
    {
        var userId = Guid.Parse(
            principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? principal.FindFirstValue("sub")!);
        var note = await db.StickyNotes.FirstOrDefaultAsync(n => n.Id == id && n.UserId == userId, ct);
        if (note is null) return false;
        db.StickyNotes.Remove(note);
        await db.SaveChangesAsync(ct);
        return true;
    }

    [Authorize]
    public async Task<UserSettingsDto> UpdateGitlabSettings(
        UpdateGitlabSettingsInput input,
        [Service] LogEaseDbContext db,
        [Service] KimaiTimesheetService kimai,
        [Service] GitlabActivityClient gitlab,
        ClaimsPrincipal principal,
        CancellationToken ct)
    {
        var userId = Guid.Parse(
            principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? principal.FindFirstValue("sub")!);
        var u = await db.Users.FirstAsync(x => x.Id == userId, ct);

        if (input.PersonalAccessToken is not null)
            u.GitlabPersonalAccessToken = string.IsNullOrWhiteSpace(input.PersonalAccessToken)
                ? null
                : GitlabActivityClient.NormalizeToken(input.PersonalAccessToken);
        if (input.BaseUrl is not null)
            u.GitlabBaseUrl = string.IsNullOrWhiteSpace(input.BaseUrl)
                ? null
                : GitlabActivityClient.NormalizeBaseUrl(input.BaseUrl);
        if (input.Username is not null)
            u.GitlabUsername = string.IsNullOrWhiteSpace(input.Username) ? null : input.Username.Trim();

        if (!string.IsNullOrWhiteSpace(u.GitlabPersonalAccessToken))
        {
            try
            {
                var profile = await gitlab.ValidateTokenAsync(u, ct);
                if (profile is null)
                    throw new GraphQLException("GitLab token did not return a user profile. Check the token.");
            }
            catch (GraphQLException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new GraphQLException("GitLab rejected this token: " + ex.Message);
            }
        }

        await db.SaveChangesAsync(ct);
        return await UserSettingsMapper.ToDtoAsync(u, kimai, gitlab, ct);
    }

    [Authorize]
    public async Task<UserSettingsDto> UpdateMailSettings(
        UpdateMailSettingsInput input,
        [Service] LogEaseDbContext db,
        [Service] KimaiTimesheetService kimai,
        [Service] GitlabActivityClient gitlab,
        ClaimsPrincipal principal,
        CancellationToken ct)
    {
        var userId = Guid.Parse(
            principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? principal.FindFirstValue("sub")!);
        var u = await db.Users.FirstAsync(x => x.Id == userId, ct);

        if (input.ImapHost is not null)
            u.MailImapHost = string.IsNullOrWhiteSpace(input.ImapHost) ? null : input.ImapHost.Trim();
        if (input.ImapPort is not null)
            u.MailImapPort = input.ImapPort is > 0 ? input.ImapPort : null;
        if (input.ImapUser is not null)
            u.MailImapUser = string.IsNullOrWhiteSpace(input.ImapUser) ? null : input.ImapUser.Trim();
        if (input.ImapPassword is not null)
            u.MailImapPassword = string.IsNullOrWhiteSpace(input.ImapPassword) ? null : input.ImapPassword;
        if (input.UseSsl is not null)
            u.MailImapUseSsl = input.UseSsl.Value;

        await db.SaveChangesAsync(ct);
        return await UserSettingsMapper.ToDtoAsync(u, kimai, gitlab, ct);
    }

    /// <summary>
    /// Phase A/B: gather sticky notes (+ optional GitLab / mail digests) → AI worklog draft.
    /// </summary>
    [Authorize]
    public async Task<DraftFromSourcesPayload> DraftWorklogFromSources(
        DraftFromSourcesInput input,
        [Service] WorklogDraftFromSourcesService draft,
        [Service] ProjectCatalogService catalog,
        [Service] LogEaseDbContext db,
        [Service] KimaiTimesheetService kimai,
        [Service] WorklogStorageSettingsService storage,
        [Service] AiOpsService aiOps,
        ClaimsPrincipal principal,
        CancellationToken ct)
    {
        if (!DateOnly.TryParse(input.DateFrom, out var from) || !DateOnly.TryParse(input.DateTo, out var to))
            throw new GraphQLException("dateFrom and dateTo must be YYYY-MM-DD.");
        if (to < from)
            throw new GraphQLException("dateTo must be on or after dateFrom.");
        if (!input.IncludeNotes && !input.IncludeGitlab && !input.IncludeMail)
            throw new GraphQLException("Select at least one source: notes, GitLab, or mail.");

        var userId = Guid.Parse(
            principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? principal.FindFirstValue("sub")!);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = await draft.BuildAsync(
            userId, from, to,
            input.IncludeNotes, input.IncludeGitlab, input.IncludeMail, ct);

        var user = await db.Users.AsNoTracking().FirstAsync(x => x.Id == userId, ct);
        var saveTarget = await storage.GetSaveTargetAsync(ct);
        var useKimaiCatalog = saveTarget is WorklogSaveTarget.Kimai or WorklogSaveTarget.Both
                              && !string.IsNullOrWhiteSpace(user.KimaiApiToken)
                              && kimai.IsConfigured;

        IReadOnlyList<ParsedWorklogRow> rows;
        string? warning = result.Warning;
        if (useKimaiCatalog)
        {
            var kimaiCatalog = await kimai.GetCatalogAsync(user.KimaiApiToken!, ct);
            rows = MapRowsToKimaiCatalog(result.Rows, kimaiCatalog);
        }
        else
        {
            rows = await catalog.EnsureProjectsForRowsAsync(result.Rows, ct);
        }

        sw.Stop();
        try
        {
            await aiOps.RecordParseAsync(
                userId, result.Parser, rows.Count, (int)sw.ElapsedMilliseconds, warning, "draft-sources", ct);
        }
        catch
        {
            // Observability must not break the draft path.
        }

        return new DraftFromSourcesPayload(
            rows.Select(r => new ParsedRowDto(
                r.WorkDate.ToString("yyyy-MM-dd"),
                r.ProjectName,
                r.ActivityName,
                r.Hours,
                r.Description,
                r.Confidence,
                r.ProjectId,
                r.ActivityId)).ToList(),
            result.Parser,
            warning,
            result.SourcesUsed,
            result.CombinedPreview);
    }

    [Authorize]
    public async Task<OpsIncidentDto> CreateOpsIncident(
        CreateOpsIncidentInput input,
        [Service] LogEaseDbContext db,
        ClaimsPrincipal principal,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input.Title))
            throw new GraphQLException("Title is required.");
        var userId = Guid.Parse(
            principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? principal.FindFirstValue("sub")!);
        var now = DateTime.UtcNow;
        var severity = NormalizeSeverity(input.Severity);
        var category = string.IsNullOrWhiteSpace(input.Category) ? "Other" : input.Category.Trim();
        var item = new OpsIncident
        {
            Id = Guid.NewGuid(),
            Title = input.Title.Trim(),
            Detail = (input.Detail ?? "").Trim(),
            Severity = severity,
            Status = "Open",
            Category = Truncate(category, 40),
            CreatedByUserId = userId,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.OpsIncidents.Add(item);
        await db.SaveChangesAsync(ct);
        return ToIncidentDto(item);
    }

    [Authorize]
    public async Task<OpsIncidentDto> UpdateOpsIncident(
        UpdateOpsIncidentInput input,
        [Service] LogEaseDbContext db,
        CancellationToken ct)
    {
        var item = await db.OpsIncidents.FirstOrDefaultAsync(x => x.Id == input.Id, ct)
                   ?? throw new GraphQLException("Incident not found.");
        if (input.Detail is not null)
            item.Detail = input.Detail.Trim();
        if (!string.IsNullOrWhiteSpace(input.Severity))
            item.Severity = NormalizeSeverity(input.Severity);
        if (!string.IsNullOrWhiteSpace(input.Status))
        {
            item.Status = NormalizeIncidentStatus(input.Status);
            item.ResolvedAt = item.Status == "Resolved" ? DateTime.UtcNow : null;
        }
        item.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return ToIncidentDto(item);
    }

    [Authorize]
    public async Task<TechDebtItemDto> CreateTechDebtItem(
        CreateTechDebtInput input,
        [Service] LogEaseDbContext db,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input.Title))
            throw new GraphQLException("Title is required.");
        var now = DateTime.UtcNow;
        var item = new TechDebtItem
        {
            Id = Guid.NewGuid(),
            Title = input.Title.Trim(),
            Description = (input.Description ?? "").Trim(),
            Area = string.IsNullOrWhiteSpace(input.Area) ? "AI" : Truncate(input.Area.Trim(), 40),
            Priority = NormalizePriority(input.Priority),
            Status = "Open",
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.TechDebtItems.Add(item);
        await db.SaveChangesAsync(ct);
        return ToDebtDto(item);
    }

    [Authorize]
    public async Task<TechDebtItemDto> UpdateTechDebtItem(
        UpdateTechDebtInput input,
        [Service] LogEaseDbContext db,
        CancellationToken ct)
    {
        var item = await db.TechDebtItems.FirstOrDefaultAsync(x => x.Id == input.Id, ct)
                   ?? throw new GraphQLException("Tech debt item not found.");
        if (input.Description is not null)
            item.Description = input.Description.Trim();
        if (!string.IsNullOrWhiteSpace(input.Priority))
            item.Priority = NormalizePriority(input.Priority);
        if (!string.IsNullOrWhiteSpace(input.Status))
            item.Status = NormalizeDebtStatus(input.Status);
        item.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return ToDebtDto(item);
    }

    private static OpsIncidentDto ToIncidentDto(OpsIncident i) =>
        new(i.Id, i.Title, i.Detail, i.Severity, i.Status, i.Category, i.RelatedParseEventId,
            i.CreatedAt.ToString("o"), i.UpdatedAt.ToString("o"),
            i.ResolvedAt?.ToString("o"));

    private static TechDebtItemDto ToDebtDto(TechDebtItem i) =>
        new(i.Id, i.Title, i.Description, i.Area, i.Priority, i.Status,
            i.CreatedAt.ToString("o"), i.UpdatedAt.ToString("o"));

    private static string NormalizeSeverity(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "low" => "Low",
            "high" => "High",
            _ => "Medium",
        };

    private static string NormalizePriority(string? value) => NormalizeSeverity(value);

    private static string NormalizeIncidentStatus(string value) =>
        value.Trim().ToLowerInvariant() switch
        {
            "investigating" => "Investigating",
            "mitigated" => "Mitigated",
            "resolved" => "Resolved",
            _ => "Open",
        };

    private static string NormalizeDebtStatus(string value) =>
        value.Trim().ToLowerInvariant() switch
        {
            "inprogress" or "in-progress" or "in progress" => "InProgress",
            "done" => "Done",
            _ => "Open",
        };

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..(max - 1)] + "…";

    private static StickyNoteDto ToNoteDto(StickyNote n) =>
        new(n.Id, n.NoteDate.ToString("yyyy-MM-dd"), n.Body, n.CreatedAt.ToString("o"), n.UpdatedAt.ToString("o"));
}
