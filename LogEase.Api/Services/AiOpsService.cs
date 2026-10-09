using LogEase.Api.Data;
using LogEase.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace LogEase.Api.Services;

/// <summary>
/// Records AI parse observability events and opens incidents on hard fallbacks.
/// </summary>
public class AiOpsService
{
    private readonly LogEaseDbContext _db;
    private readonly ILogger<AiOpsService> _logger;

    public AiOpsService(LogEaseDbContext db, ILogger<AiOpsService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<AiParseEvent> RecordParseAsync(
        Guid userId,
        string parser,
        int rowCount,
        int durationMs,
        string? warning,
        string source,
        CancellationToken ct)
    {
        var usedFallback = string.Equals(parser, "demo-heuristic", StringComparison.OrdinalIgnoreCase);
        var evt = new AiParseEvent
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Parser = Truncate(parser, 80),
            RowCount = rowCount,
            DurationMs = Math.Max(0, durationMs),
            UsedFallback = usedFallback,
            Success = rowCount > 0,
            Warning = string.IsNullOrWhiteSpace(warning) ? null : Truncate(warning, 500),
            Source = Truncate(source, 40),
            CreatedAt = DateTime.UtcNow,
        };
        _db.AiParseEvents.Add(evt);

        // Incident response: when AI chain fails and we land on heuristic, open (or refresh) an incident.
        if (usedFallback && !string.IsNullOrWhiteSpace(warning))
        {
            var openSame = await _db.OpsIncidents
                .Where(i => i.Status != "Resolved"
                            && i.Category == "AI"
                            && i.Title == "AI parse fell back to heuristic")
                .OrderByDescending(i => i.CreatedAt)
                .FirstOrDefaultAsync(ct);

            if (openSame is null)
            {
                _db.OpsIncidents.Add(new OpsIncident
                {
                    Id = Guid.NewGuid(),
                    Title = "AI parse fell back to heuristic",
                    Detail = Truncate(
                        $"Parser={parser}. Warning={warning}. User={userId:N}. Rows={rowCount}. Source={source}.",
                        2000),
                    Severity = "High",
                    Status = "Open",
                    Category = "AI",
                    RelatedParseEventId = evt.Id,
                    CreatedByUserId = userId,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                });
                _logger.LogWarning("Opened AI fallback incident for parse {EventId}", evt.Id);
            }
            else
            {
                openSame.Detail = Truncate(
                    $"Latest: Parser={parser}. Warning={warning}. Rows={rowCount}. Source={source}.",
                    2000);
                openSame.RelatedParseEventId = evt.Id;
                openSame.UpdatedAt = DateTime.UtcNow;
                if (openSame.Status == "Mitigated")
                    openSame.Status = "Open";
            }
        }

        await _db.SaveChangesAsync(ct);
        return evt;
    }

    public async Task EnsureSeedDebtAsync(CancellationToken ct)
    {
        if (await _db.TechDebtItems.AnyAsync(ct))
            return;

        var now = DateTime.UtcNow;
        _db.TechDebtItems.AddRange(
            new TechDebtItem
            {
                Id = Guid.Parse("aaaaaaaa-0001-0001-0001-000000000001"),
                Title = "No golden eval suite for worklog parse",
                Description =
                    "Prompt/model changes ship without a fixed set of messy notes → expected rows. Demo risk: regressions are found by users.",
                Area = "AI",
                Priority = "High",
                Status = "Open",
                CreatedAt = now,
                UpdatedAt = now,
            },
            new TechDebtItem
            {
                Id = Guid.Parse("aaaaaaaa-0001-0001-0001-000000000002"),
                Title = "Kimai sync still partially stubbed in places",
                Description =
                    "Office can post to Kimai, but edge cases (catalog mismatch, token expiry) need clearer incident paths.",
                Area = "API",
                Priority = "Medium",
                Status = "Open",
                CreatedAt = now,
                UpdatedAt = now,
            },
            new TechDebtItem
            {
                Id = Guid.Parse("aaaaaaaa-0001-0001-0001-000000000003"),
                Title = "AI parse instructions not versioned",
                Description =
                    "Customize-AI wording is a free-text field with no history. Hard to roll back a bad instruction change.",
                Area = "AI",
                Priority = "Medium",
                Status = "Open",
                CreatedAt = now,
                UpdatedAt = now,
            },
            new TechDebtItem
            {
                Id = Guid.Parse("aaaaaaaa-0001-0001-0001-000000000004"),
                Title = "Heuristic fallback quality varies",
                Description =
                    "Offline demo-heuristic keeps the UI alive but row quality is uneven — tracked until provider health improves.",
                Area = "AI",
                Priority = "Low",
                Status = "InProgress",
                CreatedAt = now,
                UpdatedAt = now,
            });

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Seeded LogEase technical debt starter items.");
    }

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..(max - 1)] + "…";
}
