using LogEase.Api.Data;
using LogEase.Api.Entities;
using LogEase.Api.Integrations.Gitlab;
using LogEase.Api.Integrations.Mail;
using LogEase.Api.Services;
using Microsoft.EntityFrameworkCore;
using System.Text;

namespace LogEase.Api.Services;

public sealed class WorklogDraftFromSourcesService
{
    private readonly LogEaseDbContext _db;
    private readonly WorklogParseService _parser;
    private readonly GitlabActivityClient _gitlab;
    private readonly MailActivityClient _mail;

    public WorklogDraftFromSourcesService(
        LogEaseDbContext db,
        WorklogParseService parser,
        GitlabActivityClient gitlab,
        MailActivityClient mail)
    {
        _db = db;
        _parser = parser;
        _gitlab = gitlab;
        _mail = mail;
    }

    public async Task<SourceDraftResult> BuildAsync(
        Guid userId,
        DateOnly from,
        DateOnly to,
        bool includeNotes,
        bool includeGitlab,
        bool includeMail,
        CancellationToken ct)
    {
        if (to < from)
            throw new ArgumentException("dateTo must be on or after dateFrom.");

        var user = await _db.Users.AsNoTracking().FirstAsync(x => x.Id == userId, ct);
        var parts = new List<string>();
        var sourcesUsed = new List<string>();
        var warnings = new List<string>();

        if (includeNotes)
        {
            var notes = await _db.StickyNotes.AsNoTracking()
                .Where(n => n.UserId == userId && n.NoteDate >= from && n.NoteDate <= to)
                .OrderBy(n => n.NoteDate)
                .ToListAsync(ct);
            if (notes.Count > 0)
            {
                var sb = new StringBuilder();
                sb.AppendLine("## Sticky notes");
                foreach (var n in notes)
                    sb.AppendLine($"{n.NoteDate:yyyy-MM-dd}: {n.Body.Trim()}");
                parts.Add(sb.ToString());
                sourcesUsed.Add($"notes:{notes.Count}");
            }
            else
            {
                warnings.Add("No sticky notes in this period.");
            }
        }

        if (includeGitlab)
        {
            if (string.IsNullOrWhiteSpace(user.GitlabPersonalAccessToken))
            {
                warnings.Add("GitLab token not configured in Settings.");
            }
            else
            {
                try
                {
                    var digest = await _gitlab.FetchActivityDigestAsync(user, from, to, ct);
                    if (!string.IsNullOrWhiteSpace(digest))
                    {
                        parts.Add(digest);
                        sourcesUsed.Add("gitlab");
                    }
                }
                catch (Exception ex)
                {
                    warnings.Add("GitLab: " + ex.Message);
                }
            }
        }

        if (includeMail)
        {
            if (string.IsNullOrWhiteSpace(user.MailImapHost)
                || string.IsNullOrWhiteSpace(user.MailImapUser)
                || string.IsNullOrWhiteSpace(user.MailImapPassword))
            {
                warnings.Add("Mail IMAP not configured in Settings.");
            }
            else
            {
                try
                {
                    var digest = await _mail.FetchMailDigestAsync(user, from, to, ct);
                    if (!string.IsNullOrWhiteSpace(digest))
                    {
                        parts.Add(digest);
                        sourcesUsed.Add("mail");
                    }
                }
                catch (Exception ex)
                {
                    warnings.Add("Mail: " + ex.Message);
                }
            }
        }

        if (parts.Count == 0)
        {
            return new SourceDraftResult(
                Array.Empty<ParsedWorklogRow>(),
                "none",
                "No source text to summarize. Add sticky notes and/or configure GitLab/mail.",
                string.Join("; ", sourcesUsed),
                string.Join(" ", warnings));
        }

        var combined =
            $"Summarize these activity sources into worklog rows for {from:yyyy-MM-dd} to {to:yyyy-MM-dd}. " +
            "Group related work, estimate hours reasonably (workdays), map to likely projects.\n\n" +
            string.Join("\n\n", parts);

        var parsed = await _parser.ParseAsync(combined, from, to, ct, user.AiParseInstructions);
        var warn = string.Join(" ", new[] { parsed.Warning }.Concat(warnings).Where(w => !string.IsNullOrWhiteSpace(w)));
        return new SourceDraftResult(
            parsed.Rows,
            parsed.Parser,
            string.IsNullOrWhiteSpace(warn) ? null : warn,
            string.Join(", ", sourcesUsed),
            combined.Length > 4000 ? combined[..4000] + "…" : combined);
    }
}

public sealed record SourceDraftResult(
    IReadOnlyList<ParsedWorklogRow> Rows,
    string Parser,
    string? Warning,
    string SourcesUsed,
    string? CombinedPreview);
