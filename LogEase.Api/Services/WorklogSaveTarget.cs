namespace LogEase.Api.Services;

/// <summary>
/// Where Save posts employee worklogs.
/// Values match appsettings WorklogStorage:SaveTarget and Admin settings.
/// </summary>
public enum WorklogSaveTarget
{
    /// <summary>Save only to LogEase SQL Server.</summary>
    LocalServer = 0,

    /// <summary>Post only to Kimai (no local worklog row).</summary>
    Kimai = 1,

    /// <summary>Save to LogEase SQL Server and post to Kimai.</summary>
    Both = 2,
}

public static class WorklogSaveTargetParser
{
    public const string SettingKey = "WorklogSaveTarget";

    public static WorklogSaveTarget Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return WorklogSaveTarget.LocalServer;

        return value.Trim().ToLowerInvariant() switch
        {
            "localserver" or "local" or "database" or "sql" or "sqlserver" => WorklogSaveTarget.LocalServer,
            "kimai" => WorklogSaveTarget.Kimai,
            "both" or "kimaiandlocal" or "localandkimai" => WorklogSaveTarget.Both,
            _ => WorklogSaveTarget.LocalServer,
        };
    }

    public static string ToConfigValue(WorklogSaveTarget target) => target switch
    {
        WorklogSaveTarget.Kimai => "Kimai",
        WorklogSaveTarget.Both => "Both",
        _ => "LocalServer",
    };

    public static string ToDisplayName(WorklogSaveTarget target) => target switch
    {
        WorklogSaveTarget.Kimai => "Kimai only",
        WorklogSaveTarget.Both => "Local server and Kimai",
        _ => "Local server only",
    };
}
