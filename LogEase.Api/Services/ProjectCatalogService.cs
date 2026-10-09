using LogEase.Api.Data;
using LogEase.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace LogEase.Api.Services;

public class ProjectCatalogService
{
    private readonly LogEaseDbContext _db;

    public ProjectCatalogService(LogEaseDbContext db) => _db = db;

    /// <summary>
    /// Remap rows to catalog projects. Prefer exact / fuzzy match over creating duplicates
    /// when the name is a near-miss spelling of an existing project.
    /// </summary>
    public async Task<IReadOnlyList<ParsedWorklogRow>> EnsureProjectsForRowsAsync(
        IReadOnlyList<ParsedWorklogRow> rows,
        CancellationToken ct)
    {
        if (rows.Count == 0) return rows;

        var all = await _db.Projects.Include(p => p.Activities).ToListAsync(ct);
        var result = new List<ParsedWorklogRow>();

        foreach (var row in rows)
        {
            var name = (row.ProjectName ?? "").Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                result.Add(row);
                continue;
            }

            var project = FindBestMatch(all, name);

            if (project is null)
            {
                project = new Project
                {
                    Id = Guid.NewGuid(),
                    Name = ToTitle(name),
                    IsActive = true,
                    Activities = DefaultActivities().ToList(),
                };
                foreach (var a in project.Activities)
                    a.ProjectId = project.Id;

                _db.Projects.Add(project);
                await _db.SaveChangesAsync(ct);
                all.Add(project);
            }

            var activity = project.Activities.FirstOrDefault(a =>
                               a.Name.Equals(row.ActivityName, StringComparison.OrdinalIgnoreCase))
                           ?? project.Activities.FirstOrDefault(a =>
                               a.Name.Contains("Dev", StringComparison.OrdinalIgnoreCase))
                           ?? project.Activities.First();

            result.Add(row with
            {
                ProjectName = project.Name,
                ProjectId = project.Id,
                ActivityName = activity.Name,
                ActivityId = activity.Id,
            });
        }

        return result;
    }

    /// <summary>
    /// Create named projects only when they are not an exact/fuzzy match of an existing one.
    /// </summary>
    public async Task EnsureNamedProjectsAsync(IEnumerable<string> names, CancellationToken ct)
    {
        var all = await _db.Projects.Include(p => p.Activities).ToListAsync(ct);

        foreach (var raw in names)
        {
            var name = ToTitle(raw.Trim());
            if (name.Length < 2) continue;
            if (FindBestMatch(all, name) is not null) continue;

            var project = new Project
            {
                Id = Guid.NewGuid(),
                Name = name,
                IsActive = true,
                Activities = DefaultActivities().ToList(),
            };
            foreach (var a in project.Activities)
                a.ProjectId = project.Id;

            _db.Projects.Add(project);
            await _db.SaveChangesAsync(ct);
            all.Add(project);
        }
    }

    public async Task<Project> CreateProjectAsync(string name, CancellationToken ct)
    {
        name = ToTitle((name ?? "").Trim());
        if (name.Length < 2)
            throw new ArgumentException("Project name is required.");

        var all = await _db.Projects.Include(p => p.Activities).ToListAsync(ct);
        var existing = FindBestMatch(all, name);
        if (existing is not null)
        {
            if (!existing.IsActive)
            {
                existing.IsActive = true;
                await _db.SaveChangesAsync(ct);
            }
            return existing;
        }

        var project = new Project
        {
            Id = Guid.NewGuid(),
            Name = name,
            IsActive = true,
            Activities = DefaultActivities().ToList(),
        };
        foreach (var a in project.Activities)
            a.ProjectId = project.Id;

        _db.Projects.Add(project);
        await _db.SaveChangesAsync(ct);
        return project;
    }

    public async Task<Project?> SetProjectActiveAsync(Guid id, bool active, CancellationToken ct)
    {
        var project = await _db.Projects.Include(p => p.Activities).FirstOrDefaultAsync(p => p.Id == id, ct);
        if (project is null) return null;
        project.IsActive = active;
        await _db.SaveChangesAsync(ct);
        return project;
    }

    /// <summary>
    /// Exact (ignore case) first, then fuzzy spelling match (e.g. "Perception Survery" → "Perception Survey").
    /// </summary>
    public static Project? FindBestMatch(IReadOnlyList<Project> projects, string rawName)
    {
        var name = (rawName ?? "").Trim();
        if (name.Length == 0) return null;

        var exact = projects.FirstOrDefault(p =>
            p.IsActive && p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (exact is not null) return exact;

        var normName = Normalize(name);
        Project? best = null;
        var bestScore = 0;

        foreach (var p in projects.Where(p => p.IsActive))
        {
            var score = FuzzyScore(p.Name, name, normName);
            if (score > bestScore)
            {
                bestScore = score;
                best = p;
            }
        }

        // Threshold: strong enough to treat as the same project (typo), not a new one
        return bestScore >= 55 ? best : null;
    }

    public static int FuzzyScore(string projectName, string rawInput, string? normInput = null)
    {
        var normProj = Normalize(projectName);
        var normText = normInput ?? Normalize(rawInput);
        if (normProj.Length == 0 || normText.Length == 0) return 0;

        if (normText == normProj) return 100;
        if (normText.Contains(normProj) || normProj.Contains(normText)) return 90;

        var dist = Levenshtein(normText, normProj);
        var maxLen = Math.Max(normText.Length, normProj.Length);
        var similarity = 1.0 - (double)dist / maxLen;

        var score = 0;
        if (similarity >= 0.85) score = 80;
        else if (similarity >= 0.75) score = 65;
        else if (similarity >= 0.65 && maxLen >= 8) score = 55;

        // Token-level: "perception survery" vs "Perception Survey"
        var inputTokens = Tokenize(rawInput);
        var projTokens = Tokenize(projectName);
        var tokenHits = 0;
        foreach (var pt in projTokens)
        {
            if (pt.Length < 3) continue;
            foreach (var it in inputTokens)
            {
                if (it == pt || (Math.Max(it.Length, pt.Length) >= 5 && Levenshtein(it, pt) <= 2))
                {
                    tokenHits++;
                    break;
                }
            }
        }
        if (projTokens.Count > 0 && tokenHits == projTokens.Count(t => t.Length >= 3))
            score = Math.Max(score, 75);
        else if (tokenHits >= 2)
            score = Math.Max(score, 50 + tokenHits * 8);

        return score;
    }

    private static List<string> Tokenize(string s) =>
        Normalize(s)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim())
            .Where(t => t.Length > 0)
            .ToList();

    private static string Normalize(string s)
    {
        var chars = s.ToLowerInvariant().Where(ch => char.IsLetterOrDigit(ch) || ch == ' ').ToArray();
        return string.Join(' ', new string(chars).Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static int Levenshtein(string a, string b)
    {
        var n = a.Length;
        var m = b.Length;
        var d = new int[n + 1, m + 1];
        for (var i = 0; i <= n; i++) d[i, 0] = i;
        for (var j = 0; j <= m; j++) d[0, j] = j;
        for (var i = 1; i <= n; i++)
        {
            for (var j = 1; j <= m; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(
                    Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                    d[i - 1, j - 1] + cost);
            }
        }
        return d[n, m];
    }

    private static IEnumerable<Activity> DefaultActivities() =>
    [
        new Activity { Id = Guid.NewGuid(), Name = "Development" },
        new Activity { Id = Guid.NewGuid(), Name = "Bug Fix" },
        new Activity { Id = Guid.NewGuid(), Name = "Support" },
        new Activity { Id = Guid.NewGuid(), Name = "Meeting" },
    ];

    private static string ToTitle(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return s;
        var parts = s.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(" ", parts.Select(p =>
            p.Length == 0 ? p : char.ToUpperInvariant(p[0]) + p[1..].ToLowerInvariant()));
    }
}
