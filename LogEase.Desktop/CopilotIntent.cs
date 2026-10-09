namespace LogEase.Desktop;

internal static class CopilotIntent
{
    internal enum Kind
    {
        History,
        SourcesAll,
        SourcesNotes,
        SourcesGitlab,
        SourcesMail,
        Parse,
    }

    internal static Kind Detect(string message)
    {
        var m = message.ToLowerInvariant();
        if (ContainsAny(m, "worklog", "saved", "already logged", "what did i do", "past work", "history", "logged"))
            return Kind.History;
        if (ContainsAny(m, "gitlab", "git lab", "commits", "merge request"))
            return Kind.SourcesGitlab;
        if (ContainsAny(m, "mail", "email", "inbox", "imap", "outlook"))
            return Kind.SourcesMail;
        if (ContainsAny(m, "notes only", "sticky notes only", "just notes"))
            return Kind.SourcesNotes;
        if (ContainsAny(m, "summarize", "summary", "week", "month", "sticky", "notes", "all sources", "everything", "draft"))
            return Kind.SourcesAll;
        return Kind.Parse;
    }

    private static bool ContainsAny(string haystack, params string[] needles)
    {
        foreach (var n in needles)
        {
            if (haystack.Contains(n, StringComparison.Ordinal))
                return true;
        }
        return false;
    }
}
