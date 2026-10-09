using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using LogEase.Api.Data;
using LogEase.Api.Integrations.Llm;
using Microsoft.EntityFrameworkCore;

namespace LogEase.Api.Services;

public record ParsedWorklogRow(
    DateOnly WorkDate,
    string ProjectName,
    string ActivityName,
    decimal Hours,
    string Description,
    string Confidence,
    Guid? ProjectId,
    Guid? ActivityId);

public record ParseResult(IReadOnlyList<ParsedWorklogRow> Rows, string Parser, string? Warning = null);

public class WorklogParseService
{
    private readonly LogEaseDbContext _db;
    private readonly ILlmChatClient _llmChatClient;
    private readonly ILogger<WorklogParseService> _logger;

    public WorklogParseService(
        LogEaseDbContext db,
        ILlmChatClient llmChatClient,
        ILogger<WorklogParseService> logger)
    {
        _db = db;
        _llmChatClient = llmChatClient;
        _logger = logger;
    }

    public async Task<ParseResult> ParseAsync(
        string text,
        DateOnly dateFrom,
        DateOnly dateTo,
        CancellationToken ct,
        string? customAiInstructions = null)
    {
        var projects = await _db.Projects
            .AsNoTracking()
            .Include(p => p.Activities)
            .Where(p => p.IsActive)
            .ToListAsync(ct);

        string? warning = null;
        var teamInstructions = string.IsNullOrWhiteSpace(customAiInstructions)
            ? AiParseInstructionDefaults.Text
            : customAiInstructions.Trim();

        // AI-first. UI From/To are binding unless the prompt names an explicit period (e.g. "only June").
        try
        {
            var (aiRows, provider) = await ParseWithLlmAsync(
                text, dateFrom, dateTo, projects, teamInstructions, ct);
            aiRows = SanitizeAiRows(aiRows, text, projects);
            if (!PromptNamesExplicitPeriod(text))
            {
                var before = aiRows.Count;
                aiRows = ClampRowsToUiRange(aiRows, dateFrom, dateTo);
                _logger.LogInformation(
                    "Prompt has no dates — forcing UI range {From}..{To} (rows {Before} → {After})",
                    dateFrom, dateTo, before, aiRows.Count);
            }
            if (aiRows.Count > 0)
            {
                _logger.LogInformation(
                    "{Provider} returned {Count} rows spanning {From}..{To}",
                    provider,
                    aiRows.Count,
                    aiRows.Min(r => r.WorkDate),
                    aiRows.Max(r => r.WorkDate));
                return new ParseResult(aiRows, provider);
            }

            warning = $"{provider} returned no usable rows.";
            _logger.LogWarning("{Provider} returned no usable rows; falling back.", provider);
        }
        catch (Exception ex)
        {
            warning = Truncate(ex.Message, 280);
            _logger.LogWarning(ex, "LLM parse failed; falling back to heuristic.");
        }

        // Offline fallback: expand period from prompt keywords only when present; else keep UI dates.
        (dateFrom, dateTo) = ResolvePeriod(text, dateFrom, dateTo);
        var workdays = EnumerateWorkdays(dateFrom, dateTo);
        _logger.LogInformation("Heuristic window {From}..{To} ({Days} workdays)", dateFrom, dateTo, workdays.Count);

        var heuristic = ParseHeuristic(text, workdays, projects);
        return new ParseResult(PostProcess(heuristic, text, workdays, projects), "demo-heuristic", warning);
    }

    /// <summary>
    /// True when the free text itself defines the calendar window (month name, N weeks, etc.).
    /// When false, UI From/To must be respected.
    /// </summary>
    public static bool PromptNamesExplicitPeriod(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var t = text.ToLowerInvariant();

        // Relative / numeric periods
        if (Regex.IsMatch(t, @"\b(\d+)\s*weeks?\b") || t.Contains("two week") || t.Contains("2 week")
            || t.Contains("last week") || t.Contains("this week") || t.Contains("next week")
            || t.Contains("last 2 week") || t.Contains("past 2 week"))
            return true;

        if (t.Contains("this month") || t.Contains("whole month") || t.Contains("for a month")
            || t.Contains("for the month") || t.Contains("last month") || Regex.IsMatch(t, @"\b(\d+)\s*months?\b"))
            return true;

        // Month names only with period context (avoid false hits like English "may")
        const string months =
            @"jan(?:uary)?|feb(?:ruary)?|mar(?:ch)?|apr(?:il)?|may|jun(?:e)?|jul(?:y)?|aug(?:ust)?|sep(?:t(?:ember)?)?|oct(?:ober)?|nov(?:ember)?|dec(?:ember)?";
        if (Regex.IsMatch(t, $@"\b(?:only\s+for|only|for\s+the\s+month\s+of|month\s+of|during|in|throughout)\s+({months})\b")
            || Regex.IsMatch(t, $@"\b({months})\s+(?:month|only)\b")
            || Regex.IsMatch(t, $@"\b({months})\s+20\d{{2}}\b"))
            return true;

        // Explicit calendar dates in the text
        if (Regex.IsMatch(t, @"\b20\d{2}-\d{2}-\d{2}\b") || Regex.IsMatch(t, @"\b\d{1,2}[/-]\d{1,2}[/-]\d{2,4}\b"))
            return true;

        if (Regex.IsMatch(t, @"\bfrom\s+\d") || Regex.IsMatch(t, @"\bbetween\s+\d"))
            return true;

        return false;
    }

    /// <summary>If user says 2 weeks / month, expand the date window backward from dateTo.</summary>
    public static (DateOnly From, DateOnly To) ResolvePeriod(string text, DateOnly dateFrom, DateOnly dateTo)
    {
        var t = text.ToLowerInvariant();
        var to = dateTo;
        DateOnly from = dateFrom;

        if (!PromptNamesExplicitPeriod(text))
            return (from, to);

        if (Regex.IsMatch(t, @"\b(\d+)\s*weeks?\b") || t.Contains("two week") || t.Contains("2 week") || t.Contains("for 2 week") || t.Contains("for two week"))
        {
            var m = Regex.Match(t, @"\b(\d+)\s*weeks?\b");
            var weeks = m.Success && int.TryParse(m.Groups[1].Value, out var w) ? Math.Clamp(w, 1, 12) : 2;
            from = to.AddDays(-(weeks * 7 - 1));
        }
        else if (t.Contains("this month") || t.Contains("whole month") || t.Contains("for a month") || t.Contains("for the month") || Regex.IsMatch(t, @"\b(\d+)\s*months?\b"))
        {
            var m = Regex.Match(t, @"\b(\d+)\s*months?\b");
            var months = m.Success && int.TryParse(m.Groups[1].Value, out var mo) ? Math.Clamp(mo, 1, 6) : 1;
            from = to.AddDays(-(months * 30 - 1));
        }
        else if (t.Contains("last week"))
        {
            from = to.AddDays(-6);
        }

        if (from > to) from = to;
        return (from, to);
    }

    private static List<ParsedWorklogRow> ClampRowsToUiRange(
        IReadOnlyList<ParsedWorklogRow> rows,
        DateOnly from,
        DateOnly to)
    {
        var clamped = rows
            .Where(r => r.WorkDate >= from && r.WorkDate <= to)
            .ToList();
        if (clamped.Count == 0 && rows.Count > 0)
        {
            // Model invented a different window — remap onto UI workdays keeping project/hours/desc.
            var workdays = EnumerateWorkdays(from, to);
            if (workdays.Count == 0) return clamped;

            var byProject = rows
                .GroupBy(r => r.ProjectName, StringComparer.OrdinalIgnoreCase)
                .ToList();
            clamped = [];
            foreach (var day in workdays)
            {
                foreach (var group in byProject)
                {
                    var sample = group.OrderByDescending(r => r.Hours).First();
                    clamped.Add(sample with { WorkDate = day, Confidence = "medium" });
                }
            }
        }

        return clamped
            .OrderBy(r => r.WorkDate)
            .ThenBy(r => r.ProjectName)
            .ToList();
    }

    private async Task<(IReadOnlyList<ParsedWorklogRow> Rows, string Provider)> ParseWithLlmAsync(
        string text,
        DateOnly uiDateFrom,
        DateOnly uiDateTo,
        List<Entities.Project> projects,
        string teamInstructions,
        CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var catalog = projects.Select(p => new
        {
            project = p.Name,
            activities = p.Activities.Select(a => a.Name).ToArray(),
            aliases = BuildAliases(p.Name),
        });

        var detectedProjects = DetectProjectsInText(text, projects)
            .Select(p => p.Name)
            .ToList();

        var promptOwnsPeriod = PromptNamesExplicitPeriod(text);
        var dateRules = promptOwnsPeriod
            ? """
            DATE RULES:
            - The employee named a period in the text (month / weeks / explicit dates). Infer that period from their words.
            - Prefer weekdays (Mon–Fri). Skip weekends unless they explicitly logged weekend work.
            - UI dateFrom/dateTo are hints only when they conflict with a named month/period in the prompt.
            """
            : $$"""
            DATE RULES (STRICT — UI range is mandatory):
            - The prompt does NOT name a calendar period. You MUST use ONLY dates from {{uiDateFrom:yyyy-MM-dd}} through {{uiDateTo:yyyy-MM-dd}} inclusive.
            - Do NOT invent earlier or later dates (e.g. do not go back to July 6 if UI says July 15–16).
            - Prefer weekdays (Mon–Fri) inside that UI range only. Skip weekends unless they explicitly logged weekend work.
            - Cover the weekdays that fall inside the UI range for every project mentioned.
            """;

        var system = $$"""
            You are LogEase, a timesheet writer. Read the employee's free text and output a complete worklog TABLE as JSON.

            OUTPUT JSON ONLY (no markdown):
            {"rows":[{"workDate":"YYYY-MM-DD","projectName":"...","activityName":"...","hours":4.0,"description":"...","confidence":"high|medium|low"}]}

            {{dateRules}}
            - Today (for relative phrases) is {{today:yyyy-MM-dd}}.

            TEAM / FORMAT INTERPRETATION (customized for this account — follow closely):
            {{teamInstructions}}

            DESCRIPTION RULES (critical):
            - Base every description ONLY on what the employee wrote in employeeFreeText.
            - Do NOT invent work that was not mentioned (no analytics, reporting, translate tables, etc. unless they said so).
            - NEVER paste the raw prompt unchanged; rewrite into a short professional line (8–18 words) that still reflects their words.
            - Example: input "regenerate survey preview mode update" → "Updated survey preview regeneration mode" (not unrelated debugging topics).
            - Vary wording across days only when covering multiple days of the same work.

            MULTI-PROJECT:
            - Create rows for EVERY project they mention ("other project is…").
            - Catalog matches (hints): {{string.Join(", ", detectedProjects.DefaultIfEmpty("(infer from text)"))}}.
            - Split daily hours across projects (e.g. 5h + 2.5h ≈ 7.5h).

            HOURS: 0.25–12 per row; typical day total 7–8 unless overtime said.

            PROJECT NAMES:
            - Prefer exact catalog names when the employee clearly means a catalog project (fix typos via aliases).
            - New project named in text → use that name.
            - activityName from catalog when possible (debugging → Bug Fix); else "Development".

            Be complete for the allowed date window × all mentioned projects.
            """;

        var userMsg = JsonSerializer.Serialize(new
        {
            today = today.ToString("yyyy-MM-dd"),
            uiDateFrom = uiDateFrom.ToString("yyyy-MM-dd"),
            uiDateTo = uiDateTo.ToString("yyyy-MM-dd"),
            promptOwnsPeriod,
            note = promptOwnsPeriod
                ? "Prompt names a period — use that. UI dates are secondary."
                : "Prompt has no period — use uiDateFrom..uiDateTo ONLY. Do not invent other dates.",
            catalog,
            detectedProjects,
            employeeFreeText = text,
        });

        var response = await _llmChatClient.CompleteAsync(system, userMsg, ct);
        return (MapJsonRows(StripCodeFence(response.Content), projects), response.Provider);
    }

    /// <summary>
    /// Light validation for AI rows only. Never rewrite AI descriptions with hardcoded templates.
    /// </summary>
    private static List<ParsedWorklogRow> SanitizeAiRows(
        IReadOnlyList<ParsedWorklogRow> rows,
        string originalText,
        List<Entities.Project> projects)
    {
        if (rows.Count == 0) return [];

        return rows
            .Where(r => r.Hours > 0)
            .Select(r =>
            {
                var hours = Math.Clamp(r.Hours, 0.25m, 12m);
                var description = string.IsNullOrWhiteSpace(r.Description)
                    ? BuildDescriptionFromInput(r.ProjectName, originalText, 0)
                    : r.Description.Trim();
                return r with { Hours = hours, Description = description };
            })
            .Where(r => r.WorkDate.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            .OrderBy(r => r.WorkDate)
            .ThenBy(r => r.ProjectName)
            .ToList();
    }

    private static List<ParsedWorklogRow> PostProcess(
        IReadOnlyList<ParsedWorklogRow> rows,
        string originalText,
        List<DateOnly> workdays,
        List<Entities.Project> projects)
    {
        if (rows.Count == 0) return rows.ToList();

        var list = rows.ToList();

        // Heuristic-only: if description is empty or is the raw prompt, rebuild from the input text.
        list = list.Select((r, i) =>
        {
            var desc = r.Description?.Trim() ?? "";
            var looksLikePrompt = desc.Length > 40 &&
                                  Normalize(originalText).Contains(Normalize(desc));
            if (string.IsNullOrWhiteSpace(desc) || looksLikePrompt)
                return r with { Description = BuildDescriptionFromInput(r.ProjectName, originalText, i) };
            return r;
        }).ToList();

        // If only one project but text mentions another known project, merge heuristic split for missing project
        var mentioned = DetectProjectsInText(originalText, projects);
        var presentIds = list.Where(r => r.ProjectId.HasValue).Select(r => r.ProjectId!.Value).ToHashSet();
        var missing = mentioned.Where(p => !presentIds.Contains(p.Id)).ToList();
        if (missing.Count > 0 && workdays.Count > 0)
        {
            foreach (var project in missing)
            {
                var activity = GuessActivity(project, originalText)
                               ?? project.Activities.FirstOrDefault(a => a.Name.Contains("Bug", StringComparison.OrdinalIgnoreCase))
                               ?? project.Activities.First();
                var i = 0;
                foreach (var day in workdays)
                {
                    list.Add(new ParsedWorklogRow(
                        day,
                        project.Name,
                        activity.Name,
                        2.5m,
                        BuildDescriptionFromInput(project.Name, originalText, i),
                        "medium",
                        project.Id,
                        activity.Id));
                    i++;
                }
                list = list.Select(r =>
                    r.ProjectId != project.Id && mentioned.Any(m => m.Id == r.ProjectId)
                        ? r with { Hours = Math.Max(2m, Math.Round(r.Hours * 0.7m, 2)) }
                        : r).ToList();
            }
        }

        return list.OrderBy(r => r.WorkDate).ThenBy(r => r.ProjectName).ToList();
    }

    /// <summary>
    /// Offline/heuristic descriptions only: derive from the employee's input text.
    /// No project-specific hardcoded slogans.
    /// </summary>
    private static string BuildDescriptionFromInput(string projectName, string originalText, int index)
    {
        var text = (originalText ?? "").Trim();
        if (string.IsNullOrWhiteSpace(text))
            return $"Work on {projectName}";

        // Prefer the work detail after "=>" / ":" when the user wrote instructions that way.
        var focus = text;
        var arrow = text.LastIndexOf("=>", StringComparison.Ordinal);
        if (arrow >= 0 && arrow < text.Length - 2)
            focus = text[(arrow + 2)..].Trim();
        else
        {
            var colon = text.IndexOf(':');
            if (colon >= 0 && colon < text.Length - 1 && colon > 8)
                focus = text[(colon + 1)..].Trim();
        }

        focus = Regex.Replace(focus, @"\b(today|yesterday|monday|tuesday|wednesday|thursday|friday|saturday|sunday)\b", " ", RegexOptions.IgnoreCase);
        focus = Regex.Replace(focus, @"\b(worked on|work on|i worked|please|add this|use suit word in description)\b", " ", RegexOptions.IgnoreCase);
        focus = Regex.Replace(focus, @"\s+", " ").Trim(' ', ',', '.', '-', '>');
        if (focus.Length < 6)
            focus = Regex.Replace(text, @"\s+", " ").Trim();

        if (focus.Length > 110)
            focus = focus[..110].Trim();

        if (string.IsNullOrWhiteSpace(focus))
            return $"Work on {projectName}";

        var sentence = char.ToUpperInvariant(focus[0]) + focus[1..];
        if (index > 0)
            sentence = $"Continued: {char.ToLowerInvariant(sentence[0])}{sentence[1..]}";
        return sentence;
    }

    private static List<ParsedWorklogRow> ParseHeuristic(
        string text,
        List<DateOnly> workdays,
        List<Entities.Project> projects)
    {
        if (workdays.Count == 0) workdays = [DateOnly.FromDateTime(DateTime.Today)];

        var mentioned = DetectProjectsInText(text, projects);
        if (mentioned.Count == 0)
            mentioned = projects.Take(1).ToList();

        // Split hours: primary project gets more if mentioned first
        var hoursMap = new Dictionary<Guid, decimal>();
        if (mentioned.Count == 1)
            hoursMap[mentioned[0].Id] = 7.5m;
        else if (mentioned.Count == 2)
        {
            hoursMap[mentioned[0].Id] = 5.0m;
            hoursMap[mentioned[1].Id] = 2.5m;
        }
        else
        {
            var each = Math.Round(7.5m / mentioned.Count, 2);
            foreach (var p in mentioned) hoursMap[p.Id] = each;
        }

        var rows = new List<ParsedWorklogRow>();
        foreach (var project in mentioned)
        {
            var activity = GuessActivity(project, text)
                           ?? project.Activities.FirstOrDefault(a => a.Name.Contains("Dev", StringComparison.OrdinalIgnoreCase))
                           ?? project.Activities.First();
            var i = 0;
            foreach (var day in workdays)
            {
                rows.Add(new ParsedWorklogRow(
                    day,
                    project.Name,
                    activity.Name,
                    hoursMap[project.Id],
                    BuildDescriptionFromInput(project.Name, text, i),
                    "medium",
                    project.Id,
                    activity.Id));
                i++;
            }
        }

        return rows;
    }

    private static List<Entities.Project> DetectProjectsInText(string text, List<Entities.Project> projects)
    {
        // Split on "other project" etc. and score each segment + whole text
        var segments = Regex.Split(text, @"\bother\s+proj\w*\b|\balso\b|\bsecond\b|\banother\b", RegexOptions.IgnoreCase)
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .ToList();
        if (segments.Count == 0) segments.Add(text);

        var found = new List<Entities.Project>();
        foreach (var seg in segments)
        {
            var best = projects
                .Select(p => new { p, score = ScoreProjectMatch(p.Name, seg) })
                .Where(x => x.score >= 8)
                .OrderByDescending(x => x.score)
                .FirstOrDefault();
            if (best is not null && found.All(f => f.Id != best.p.Id))
                found.Add(best.p);
        }

        // Also scan full text for any strong matches not yet found
        foreach (var p in projects.OrderByDescending(p => ScoreProjectMatch(p.Name, text)))
        {
            if (ScoreProjectMatch(p.Name, text) >= 10 && found.All(f => f.Id != p.Id))
                found.Add(p);
        }

        return found;
    }

    private static IReadOnlyList<ParsedWorklogRow> MapJsonRows(string raw, List<Entities.Project> projects)
    {
        using var doc = JsonDocument.Parse(raw);
        if (!doc.RootElement.TryGetProperty("rows", out var rowsEl)) return Array.Empty<ParsedWorklogRow>();

        var list = new List<ParsedWorklogRow>();
        foreach (var row in rowsEl.EnumerateArray())
        {
            var dateStr = row.TryGetProperty("workDate", out var wd) ? wd.GetString() : null;
            if (string.IsNullOrWhiteSpace(dateStr) || !DateOnly.TryParse(dateStr, out var workDate))
                continue;

            var projectName = row.TryGetProperty("projectName", out var pn) ? pn.GetString() ?? "" : "";
            var activityName = row.TryGetProperty("activityName", out var an) ? an.GetString() ?? "Development" : "Development";
            var hours = 1m;
            if (row.TryGetProperty("hours", out var h))
            {
                if (h.ValueKind == JsonValueKind.Number) hours = h.GetDecimal();
                else if (h.ValueKind == JsonValueKind.String &&
                         decimal.TryParse(h.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var hp))
                    hours = hp;
            }

            var desc = row.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "";
            var conf = row.TryGetProperty("confidence", out var c) ? c.GetString() ?? "medium" : "medium";

            var project = MatchProjectFromText(projects, projectName) ?? MatchProjectFromText(projects, projectName + " " + desc);
            var activity = project?.Activities.FirstOrDefault(a => FuzzyEquals(a.Name, activityName))
                           ?? GuessActivity(project, desc + " " + activityName)
                           ?? project?.Activities.FirstOrDefault();

            list.Add(new ParsedWorklogRow(
                workDate,
                project?.Name ?? projectName,
                activity?.Name ?? activityName,
                Math.Clamp(hours, 0.25m, 12m),
                string.IsNullOrWhiteSpace(desc) ? $"Work on {projectName}" : desc.Trim(),
                conf,
                project?.Id,
                activity?.Id));
        }

        return list;
    }

    private static List<DateOnly> EnumerateWorkdays(DateOnly from, DateOnly to)
    {
        var dates = new List<DateOnly>();
        for (var d = from; d <= to; d = d.AddDays(1))
        {
            if (d.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday)
                dates.Add(d);
        }
        return dates;
    }

    private static Entities.Activity? GuessActivity(Entities.Project? project, string text)
    {
        if (project is null) return null;
        var t = text.ToLowerInvariant();
        if (t.Contains("meet") || t.Contains("call") || t.Contains("standup"))
            return project.Activities.FirstOrDefault(a => a.Name.Contains("Meet", StringComparison.OrdinalIgnoreCase));
        if (t.Contains("bug") || t.Contains("debug") || t.Contains("fix") || t.Contains("duplicate") || t.Contains("issue"))
            return project.Activities.FirstOrDefault(a =>
                       a.Name.Contains("Bug", StringComparison.OrdinalIgnoreCase))
                   ?? project.Activities.FirstOrDefault(a => a.Name.Contains("Support", StringComparison.OrdinalIgnoreCase))
                   ?? project.Activities.FirstOrDefault(a => a.Name.Contains("Dev", StringComparison.OrdinalIgnoreCase));
        return project.Activities.FirstOrDefault(a => a.Name.Contains("Dev", StringComparison.OrdinalIgnoreCase));
    }

    private static Entities.Project? MatchProjectFromText(List<Entities.Project> projects, string line)
    {
        // Prefer catalog fuzzy matcher (handles "Perception Survery" → "Perception Survey")
        var fuzzy = ProjectCatalogService.FindBestMatch(projects, line);
        if (fuzzy is not null) return fuzzy;

        return projects
            .Select(p => new { p, score = ScoreProjectMatch(p.Name, line) })
            .Where(x => x.score > 0)
            .OrderByDescending(x => x.score)
            .Select(x => x.p)
            .FirstOrDefault();
    }

    private static int ScoreProjectMatch(string projectName, string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;

        var fuzzy = ProjectCatalogService.FuzzyScore(projectName, text);
        if (fuzzy >= 55) return fuzzy;

        var normText = Normalize(text);
        var normProj = Normalize(projectName);
        if (normText.Contains(normProj)) return 100;

        var score = fuzzy;
        foreach (var w in projectName.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (w.Length < 3) continue;
            var nw = Normalize(w);
            if (normText.Contains(nw)) score += 10;
        }

        if ((normProj.Contains("perception") || normProj.Contains("survey")) &&
            (normText.Contains("perception") || normText.Contains("survey") || normText.Contains("survery")))
            score += 15;
        if (normProj.Contains("announcement") &&
            (normText.Contains("announc") || normText.Contains("annunc")))
            score += 20;
        if (normText.Contains("translate") && normProj.Contains("announcement"))
            score += 8;
        if (normProj.Contains("logease") && normText.Contains("logease"))
            score += 15;
        if (normProj.Contains("codesharing") && (normText.Contains("codeshar") || normText.Contains("code sharing")))
            score += 15;

        return score;
    }

    private static string[] BuildAliases(string name) =>
        name.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(p => p.Length > 3)
            .Append(name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string StripCodeFence(string raw)
    {
        raw = raw.Trim();
        if (!raw.StartsWith("```")) return raw;
        raw = Regex.Replace(raw, @"^```(?:json)?\s*", "", RegexOptions.IgnoreCase);
        raw = Regex.Replace(raw, @"\s*```$", "");
        return raw.Trim();
    }

    private static string Normalize(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s.ToLowerInvariant())
            if (char.IsLetterOrDigit(ch)) sb.Append(ch);
        return sb.ToString();
    }

    private static bool FuzzyEquals(string a, string b) =>
        string.Equals(a, b, StringComparison.OrdinalIgnoreCase) || Normalize(a) == Normalize(b);

    private static string Truncate(string s, int n) => s.Length <= n ? s : s[..n];
}
