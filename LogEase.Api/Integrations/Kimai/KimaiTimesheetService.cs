using LogEase.Api.Services;

namespace LogEase.Api.Integrations.Kimai;

public sealed class KimaiTimesheetService
{
    private readonly KimaiApiClient _client;
    private readonly IConfiguration _configuration;

    public KimaiTimesheetService(KimaiApiClient client, IConfiguration configuration)
    {
        _client = client;
        _configuration = configuration;
    }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_configuration["Kimai:BaseUrl"]);

    public string? BaseUrl => _configuration["Kimai:BaseUrl"]?.Trim().TrimEnd('/');

    public async Task<IReadOnlyList<(KimaiProject Project, IReadOnlyList<KimaiActivity> Activities)>> GetCatalogAsync(
        string apiToken,
        CancellationToken cancellationToken)
    {
        if (!IsConfigured || string.IsNullOrWhiteSpace(apiToken))
            return Array.Empty<(KimaiProject, IReadOnlyList<KimaiActivity>)>();

        var baseUrl = BaseUrl!;
        var projects = await _client.GetProjectsAsync(baseUrl, apiToken, cancellationToken);
        // Prefer one activities call — per-project loops were blowing past GraphQL timeout.
        var globalActivities = await _client.GetActivitiesAsync(baseUrl, apiToken, projectId: null, cancellationToken);
        var byProject = globalActivities
            .GroupBy(a => a.ProjectId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<KimaiActivity>)g.ToList());

        var catalog = new List<(KimaiProject, IReadOnlyList<KimaiActivity>)>();
        foreach (var project in projects.OrderBy(p => p.Name))
        {
            if (!byProject.TryGetValue(project.Id, out var activities) || activities.Count == 0)
                activities = await _client.GetActivitiesAsync(baseUrl, apiToken, project.Id, cancellationToken);

            catalog.Add((project, activities));
        }

        return catalog;
    }

    public async Task<KimaiSyncResult> CreateTimesheetsAsync(
        string apiToken,
        IReadOnlyList<KimaiTimesheetDraft> drafts,
        CancellationToken cancellationToken)
    {
        if (!IsConfigured)
            return new KimaiSyncResult(false, "Kimai:BaseUrl is not configured in appsettings.", 0);

        if (string.IsNullOrWhiteSpace(apiToken))
            return new KimaiSyncResult(false, "Add your Kimai API token in Settings.", 0);

        var baseUrl = BaseUrl!;
        var catalog = await GetCatalogAsync(apiToken, cancellationToken);
        var projects = catalog.Select(c => c.Project).ToList();

        // Avoid Kimai overlap errors: never post two entries at the same begin time.
        var dayCursor = new Dictionary<DateOnly, TimeOnly>();
        if (drafts.Count > 0)
        {
            var from = drafts.Min(d => d.WorkDate);
            var to = drafts.Max(d => d.WorkDate);
            var existing = await _client.GetTimesheetsAsync(baseUrl, apiToken, from, to, cancellationToken);
            foreach (var group in existing.GroupBy(e => e.WorkDate))
            {
                // Place new rows after the longest existing block starting from 09:00.
                var usedHours = group.Sum(e => e.Hours);
                var next = new TimeOnly(9, 0).Add(TimeSpan.FromHours((double)usedHours));
                if (next.Hour >= 23)
                    next = new TimeOnly(9, 0);
                dayCursor[group.Key] = next;
            }
        }

        var created = 0;
        foreach (var draft in drafts)
        {
            KimaiProject? project = null;
            KimaiActivity? activity = null;

            if (draft.KimaiProjectId is int knownProjectId)
                project = projects.FirstOrDefault(p => p.Id == knownProjectId);

            project ??= MatchProject(projects, draft.ProjectName);
            if (project is null)
                return new KimaiSyncResult(
                    false,
                    $"Kimai project not found for '{draft.ProjectName}'. Pick a project from the Kimai list.",
                    created);

            var activities = catalog.FirstOrDefault(c => c.Project.Id == project.Id).Activities
                             ?? Array.Empty<KimaiActivity>();

            if (draft.KimaiActivityId is int knownActivityId)
                activity = activities.FirstOrDefault(a => a.Id == knownActivityId);

            activity ??= MatchActivity(activities, project.Id, draft.ActivityName);
            if (activity is null)
            {
                var available = activities.Count == 0
                    ? "(no activities on this Kimai project)"
                    : string.Join(", ", activities.Select(a => a.Name));
                return new KimaiSyncResult(
                    false,
                    $"Kimai activity not found for project '{project.Name}'. Available: {available}",
                    created);
            }

            if (!dayCursor.TryGetValue(draft.WorkDate, out var beginTime))
                beginTime = new TimeOnly(9, 0);

            try
            {
                await _client.CreateTimesheetAsync(
                    baseUrl,
                    apiToken,
                    draft.WorkDate,
                    beginTime,
                    draft.Hours,
                    project.Id,
                    activity.Id,
                    draft.Description,
                    cancellationToken);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("already have an entry", StringComparison.OrdinalIgnoreCase))
            {
                // Nudge forward 15 minutes and retry once for residual overlap.
                beginTime = beginTime.Add(TimeSpan.FromMinutes(15));
                try
                {
                    await _client.CreateTimesheetAsync(
                        baseUrl,
                        apiToken,
                        draft.WorkDate,
                        beginTime,
                        draft.Hours,
                        project.Id,
                        activity.Id,
                        draft.Description,
                        cancellationToken);
                }
                catch (InvalidOperationException retryEx) when (retryEx.Message.Contains("already have an entry", StringComparison.OrdinalIgnoreCase))
                {
                    return new KimaiSyncResult(
                        false,
                        $"Kimai already has overlapping time on {draft.WorkDate:yyyy-MM-dd}. Delete or edit that timesheet in Kimai, then save again. ({created} posted before this error.)",
                        created);
                }
            }

            dayCursor[draft.WorkDate] = beginTime.Add(TimeSpan.FromHours((double)draft.Hours));
            created++;
        }

        return new KimaiSyncResult(true, $"Posted {created} timesheet(s) to Kimai.", created);
    }

    public async Task<IReadOnlyList<KimaiTimesheetEntry>> GetTimesheetsAsync(
        string apiToken,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken)
    {
        if (!IsConfigured || string.IsNullOrWhiteSpace(apiToken))
            return Array.Empty<KimaiTimesheetEntry>();

        return await _client.GetTimesheetsAsync(BaseUrl!, apiToken, from, to, cancellationToken);
    }

    public async Task<bool> HasTimesheetOnDateAsync(
        string apiToken,
        DateOnly date,
        CancellationToken cancellationToken)
    {
        if (!IsConfigured || string.IsNullOrWhiteSpace(apiToken))
            return false;

        return await _client.HasTimesheetOnDateAsync(BaseUrl!, apiToken, date, cancellationToken);
    }

    public async Task<KimaiUserProfile?> GetCurrentUserAsync(
        string apiToken,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured || string.IsNullOrWhiteSpace(apiToken))
            return null;

        return await _client.GetCurrentUserAsync(BaseUrl!, apiToken, cancellationToken);
    }

    private static KimaiProject? MatchProject(IReadOnlyList<KimaiProject> projects, string? name)
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

    private static KimaiActivity? MatchActivity(
        IReadOnlyList<KimaiActivity> activities,
        int projectId,
        string? name)
    {
        var scoped = activities
            .Where(a => a.ProjectId == projectId || a.ProjectId == 0)
            .ToList();
        if (scoped.Count == 0)
            scoped = activities.ToList();

        if (scoped.Count == 0)
            return null;

        if (string.IsNullOrWhiteSpace(name))
            return scoped.FirstOrDefault();

        var exact = scoped.FirstOrDefault(a =>
            string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));
        if (exact is not null) return exact;

        return scoped
            .Select(a => new { Activity = a, Score = ProjectCatalogService.FuzzyScore(a.Name, name) })
            .Where(x => x.Score >= 40)
            .OrderByDescending(x => x.Score)
            .Select(x => x.Activity)
            .FirstOrDefault()
            ?? scoped.FirstOrDefault();
    }
}
