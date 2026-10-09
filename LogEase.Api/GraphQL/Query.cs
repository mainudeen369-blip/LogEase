using System.Security.Claims;
using HotChocolate;
using HotChocolate.Authorization;
using LogEase.Api.Data;
using LogEase.Api.Entities;
using LogEase.Api.Integrations.Gitlab;
using LogEase.Api.Integrations.Kimai;
using LogEase.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace LogEase.Api.GraphQL;

public class Query
{
    public string Health() => "LogEase.Api is running";

    /// <summary>Starter wording for Customize AI wording — how AI should read informal team notes.</summary>
    public string DefaultAiParseInstructions() => AiParseInstructionDefaults.Text;

    /// <summary>Public Azure AD settings for web/desktop sign-in.</summary>
    public AuthConfigDto AuthConfig([Service] IConfiguration config)
    {
        var tenantId = config["AzureAd:TenantId"]?.Trim();
        var clientId = config["AzureAd:ClientId"]?.Trim();
        var enabled = !string.IsNullOrWhiteSpace(tenantId) && !string.IsNullOrWhiteSpace(clientId);
        var instance = (config["AzureAd:Instance"] ?? "https://login.microsoftonline.com/").TrimEnd('/') + "/";
        return new AuthConfigDto(
            enabled,
            enabled ? tenantId : null,
            enabled ? clientId : null,
            instance,
            // Graph User.Read so desktop public-client / device-code token exchange works.
            ["https://graph.microsoft.com/User.Read"]);
    }

    [Authorize]
    public async Task<UserSettingsDto?> Me(
        [Service] LogEaseDbContext db,
        [Service] KimaiTimesheetService kimai,
        [Service] GitlabActivityClient gitlab,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var id = GetUserId(user);
        var u = await db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (u is null) return null;
        return await UserSettingsMapper.ToDtoAsync(u, kimai, gitlab, ct);
    }

    [Authorize]
    public async Task<AppSettingsDto> AppSettings(
        [Service] WorklogStorageSettingsService storage,
        [Service] IConfiguration config,
        CancellationToken ct)
    {
        var target = await storage.GetSaveTargetAsync(ct);
        var baseUrl = config["Kimai:BaseUrl"]?.Trim();
        return new AppSettingsDto(
            WorklogSaveTargetParser.ToConfigValue(target),
            WorklogSaveTargetParser.ToDisplayName(target),
            storage.IsKimaiBaseUrlConfigured,
            storage.IsKimaiBaseUrlConfigured ? baseUrl : null);
    }

    [Authorize]
    public async Task<IReadOnlyList<ProjectDto>> Projects(
        [Service] LogEaseDbContext db,
        [Service] KimaiTimesheetService kimai,
        [Service] WorklogStorageSettingsService storage,
        ClaimsPrincipal principal,
        CancellationToken ct,
        bool includeInactive = false)
    {
        var userId = GetUserId(principal);
        var user = await db.Users.AsNoTracking().FirstAsync(x => x.Id == userId, ct);
        var saveTarget = await storage.GetSaveTargetAsync(ct);
        var useKimaiCatalog = saveTarget is WorklogSaveTarget.Kimai or WorklogSaveTarget.Both
                              && !string.IsNullOrWhiteSpace(user.KimaiApiToken)
                              && kimai.IsConfigured;

        if (useKimaiCatalog)
        {
            var catalog = await kimai.GetCatalogAsync(user.KimaiApiToken!, ct);
            return catalog
                .Select(entry => new ProjectDto(
                    KimaiIdCodec.ToProjectGuid(entry.Project.Id),
                    entry.Project.Name,
                    entry.Activities
                        .Where(a => a.ProjectId == entry.Project.Id || a.ProjectId == 0)
                        .GroupBy(a => a.Id)
                        .Select(g => g.First())
                        .OrderBy(a => a.Name)
                        .Select(a => new ActivityDto(
                            KimaiIdCodec.ToActivityGuid(a.Id),
                            a.Name,
                            KimaiIdCodec.ToProjectGuid(entry.Project.Id)))
                        .ToList(),
                    true))
                .Where(p => includeInactive || p.Activities.Count > 0 || true)
                .OrderBy(p => p.Name)
                .ToList();
        }

        var query = db.Projects.AsNoTracking().AsQueryable();
        if (!includeInactive)
            query = query.Where(p => p.IsActive);

        return await query
            .Include(p => p.Activities)
            .OrderBy(p => p.Name)
            .Select(p => new ProjectDto(
                p.Id,
                p.Name,
                p.Activities.OrderBy(a => a.Name)
                    .Select(a => new ActivityDto(a.Id, a.Name, a.ProjectId))
                    .ToList(),
                p.IsActive))
            .ToListAsync(ct);
    }

    [Authorize]
    public async Task<IReadOnlyList<WorklogDto>> Worklogs(
        string? from,
        string? to,
        Guid? projectId,
        string? q,
        string? source,
        [Service] LogEaseDbContext db,
        [Service] KimaiTimesheetService kimai,
        [Service] WorklogStorageSettingsService storage,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var userId = GetUserId(user);
        var account = await db.Users.AsNoTracking().FirstAsync(x => x.Id == userId, ct);
        var saveTarget = await storage.GetSaveTargetAsync(ct);

        var sourceFilter = string.IsNullOrWhiteSpace(source)
            ? (WorklogSaveTarget?)null
            : WorklogSaveTargetParser.Parse(source);

        // Optional source filter (Kimai / LocalServer). When omitted, follow office save target.
        var useKimai = sourceFilter switch
        {
            WorklogSaveTarget.Kimai => true,
            WorklogSaveTarget.LocalServer or WorklogSaveTarget.Both => false,
            null => saveTarget == WorklogSaveTarget.Kimai
                    && !string.IsNullOrWhiteSpace(account.KimaiApiToken)
                    && kimai.IsConfigured,
            _ => false,
        };

        if (useKimai)
        {
            if (string.IsNullOrWhiteSpace(account.KimaiApiToken) || !kimai.IsConfigured)
                return Array.Empty<WorklogDto>();

            var fromDate = DateOnly.TryParse(from, out var parsedFrom)
                ? parsedFrom
                : DateOnly.FromDateTime(DateTime.Today.AddDays(-13));
            var toDate = DateOnly.TryParse(to, out var parsedTo)
                ? parsedTo
                : DateOnly.FromDateTime(DateTime.Today);

            var entries = await kimai.GetTimesheetsAsync(account.KimaiApiToken, fromDate, toDate, ct);
            var query = entries.AsEnumerable();

            if (!string.IsNullOrWhiteSpace(q))
            {
                var term = q.Trim();
                query = query.Where(w => w.Description.Contains(term, StringComparison.OrdinalIgnoreCase));
            }

            return query
                .OrderByDescending(w => w.WorkDate)
                .Select(w => new WorklogDto(
                    $"kimai-{w.Id}",
                    w.WorkDate.ToString("yyyy-MM-dd"),
                    w.ProjectName ?? "",
                    Guid.Empty,
                    w.ActivityName ?? "",
                    Guid.Empty,
                    w.Hours,
                    w.Description,
                    w.WorkDate.ToString("yyyy-MM-dd")))
                .ToList();
        }

        var sqlQuery = db.Worklogs.AsNoTracking()
            .Include(w => w.Project)
            .Include(w => w.Activity)
            .Where(w => w.UserId == userId);

        if (DateOnly.TryParse(from, out var fromDateSql))
            sqlQuery = sqlQuery.Where(w => w.WorkDate >= fromDateSql);
        if (DateOnly.TryParse(to, out var toDateSql))
            sqlQuery = sqlQuery.Where(w => w.WorkDate <= toDateSql);
        if (projectId is Guid pid)
            sqlQuery = sqlQuery.Where(w => w.ProjectId == pid);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            sqlQuery = sqlQuery.Where(w => w.Description.Contains(term));
        }

        return await sqlQuery
            .OrderByDescending(w => w.WorkDate)
            .ThenByDescending(w => w.CreatedAt)
            .Select(w => new WorklogDto(
                w.Id.ToString(),
                w.WorkDate.ToString("yyyy-MM-dd"),
                w.Project!.Name,
                w.ProjectId,
                w.Activity!.Name,
                w.ActivityId,
                w.Hours,
                w.Description,
                w.CreatedAt.ToString("O")))
            .ToListAsync(ct);
    }

    [Authorize]
    public async Task<ReminderStatusDto> ReminderStatus(
        [Service] LogEaseDbContext db,
        [Service] KimaiTimesheetService kimai,
        [Service] WorklogStorageSettingsService storage,
        ClaimsPrincipal principal,
        CancellationToken ct)
    {
        var userId = GetUserId(principal);
        var u = await db.Users.AsNoTracking().FirstAsync(x => x.Id == userId, ct);
        var timeStr = u.ReminderTime.ToString("HH:mm");

        if (!u.ReminderEnabled)
        {
            return new ReminderStatusDto(false, "Reminder is off.", timeStr, false);
        }

        DateTime localNow;
        try
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById(u.TimeZoneId);
            localNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
        }
        catch
        {
            localNow = DateTime.Now;
        }

        var today = DateOnly.FromDateTime(localNow);
        var nowTime = TimeOnly.FromDateTime(localNow);
        var saveTarget = await storage.GetSaveTargetAsync(ct);
        var loggedToday = saveTarget == WorklogSaveTarget.Kimai
            && !string.IsNullOrWhiteSpace(u.KimaiApiToken)
            && kimai.IsConfigured
            ? await kimai.HasTimesheetOnDateAsync(u.KimaiApiToken, today, ct)
            : await db.Worklogs.AnyAsync(w => w.UserId == userId && w.WorkDate == today, ct);
        var afterReminder = nowTime >= u.ReminderTime;
        var weekday = localNow.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday;

        var should = u.ReminderEnabled && weekday && afterReminder && !loggedToday;
        var msg = should
            ? $"It's past {timeStr} and you have no worklog for today. Log now?"
            : loggedToday
                ? "You already logged time today. Nice."
                : $"Reminder set for {timeStr}. We'll nudge you if nothing is logged.";

        return new ReminderStatusDto(should, msg, timeStr, u.ReminderEnabled);
    }

    [Authorize]
    public async Task<IReadOnlyList<StickyNoteDto>> StickyNotes(
        [Service] LogEaseDbContext db,
        ClaimsPrincipal principal,
        CancellationToken ct,
        string? from = null,
        string? to = null)
    {
        var userId = GetUserId(principal);
        var q = db.StickyNotes.AsNoTracking().Where(n => n.UserId == userId);
        if (!string.IsNullOrWhiteSpace(from) && DateOnly.TryParse(from, out var f))
            q = q.Where(n => n.NoteDate >= f);
        if (!string.IsNullOrWhiteSpace(to) && DateOnly.TryParse(to, out var t))
            q = q.Where(n => n.NoteDate <= t);

        return await q.OrderByDescending(n => n.NoteDate).ThenByDescending(n => n.UpdatedAt)
            .Select(n => new StickyNoteDto(
                n.Id,
                n.NoteDate.ToString("yyyy-MM-dd"),
                n.Body,
                n.CreatedAt.ToString("o"),
                n.UpdatedAt.ToString("o")))
            .ToListAsync(ct);
    }

    [Authorize]
    public async Task<ObservabilitySummaryDto> ObservabilitySummary(
        [Service] LogEaseDbContext db,
        CancellationToken ct,
        int recentTake = 25)
    {
        recentTake = Math.Clamp(recentTake, 5, 100);
        var events = await db.AiParseEvents.AsNoTracking()
            .OrderByDescending(e => e.CreatedAt)
            .Take(500)
            .ToListAsync(ct);

        var total = events.Count;
        var fallback = events.Count(e => e.UsedFallback);
        var success = events.Count(e => e.Success);
        var avgMs = total == 0 ? 0 : events.Average(e => (double)e.DurationMs);
        var byParser = events
            .GroupBy(e => e.Parser)
            .Select(g => new ProviderCountDto(g.Key, g.Count()))
            .OrderByDescending(x => x.Count)
            .ToList();
        var recent = events.Take(recentTake)
            .Select(e => new AiParseEventDto(
                e.Id,
                e.Parser,
                e.RowCount,
                e.DurationMs,
                e.UsedFallback,
                e.Success,
                e.Warning,
                e.Source,
                e.CreatedAt.ToString("o")))
            .ToList();

        return new ObservabilitySummaryDto(
            total,
            fallback,
            success,
            total == 0 ? 0 : Math.Round(100.0 * fallback / total, 1),
            Math.Round(avgMs, 0),
            byParser,
            recent);
    }

    [Authorize]
    public async Task<IReadOnlyList<OpsIncidentDto>> OpsIncidents(
        [Service] LogEaseDbContext db,
        CancellationToken ct,
        string? status = null)
    {
        var q = db.OpsIncidents.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(status))
            q = q.Where(i => i.Status == status);
        return await q.OrderByDescending(i => i.UpdatedAt)
            .Select(i => new OpsIncidentDto(
                i.Id,
                i.Title,
                i.Detail,
                i.Severity,
                i.Status,
                i.Category,
                i.RelatedParseEventId,
                i.CreatedAt.ToString("o"),
                i.UpdatedAt.ToString("o"),
                i.ResolvedAt == null ? null : i.ResolvedAt.Value.ToString("o")))
            .ToListAsync(ct);
    }

    [Authorize]
    public async Task<IReadOnlyList<TechDebtItemDto>> TechDebtItems(
        [Service] LogEaseDbContext db,
        CancellationToken ct,
        string? status = null)
    {
        var q = db.TechDebtItems.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(status))
            q = q.Where(i => i.Status == status);
        var items = await q.OrderByDescending(i => i.UpdatedAt).ToListAsync(ct);
        return items
            .OrderBy(i => i.Status == "Done" ? 1 : 0)
            .ThenBy(i => i.Priority switch { "High" => 0, "Medium" => 1, _ => 2 })
            .ThenByDescending(i => i.UpdatedAt)
            .Select(i => new TechDebtItemDto(
                i.Id,
                i.Title,
                i.Description,
                i.Area,
                i.Priority,
                i.Status,
                i.CreatedAt.ToString("o"),
                i.UpdatedAt.ToString("o")))
            .ToList();
    }

    [Authorize]
    public async Task<AutoTestReportDto> RunAutoTests(
        [Service] AutoTestService autoTest,
        ClaimsPrincipal principal,
        CancellationToken ct) =>
        await autoTest.RunAsync(GetUserId(principal), ct);

    private static Guid GetUserId(ClaimsPrincipal user)
    {
        var sid = user.FindFirstValue(ClaimTypes.NameIdentifier)
                  ?? user.FindFirstValue("sub");
        return Guid.Parse(sid!);
    }
}
