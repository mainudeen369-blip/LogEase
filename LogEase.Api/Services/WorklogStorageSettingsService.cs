using LogEase.Api.Data;
using LogEase.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace LogEase.Api.Services;

/// <summary>
/// Resolves office-wide worklog save destination.
/// Admin override in DB wins; otherwise appsettings WorklogStorage:SaveTarget.
/// </summary>
public sealed class WorklogStorageSettingsService
{
    private readonly LogEaseDbContext _db;
    private readonly IConfiguration _configuration;

    public WorklogStorageSettingsService(LogEaseDbContext db, IConfiguration configuration)
    {
        _db = db;
        _configuration = configuration;
    }

    public async Task<WorklogSaveTarget> GetSaveTargetAsync(CancellationToken cancellationToken)
    {
        var overrideValue = await _db.AppSettings.AsNoTracking()
            .Where(x => x.Key == WorklogSaveTargetParser.SettingKey)
            .Select(x => x.Value)
            .FirstOrDefaultAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(overrideValue))
            return WorklogSaveTargetParser.Parse(overrideValue);

        return WorklogSaveTargetParser.Parse(_configuration["WorklogStorage:SaveTarget"]);
    }

    public async Task<WorklogSaveTarget> SetSaveTargetAsync(
        WorklogSaveTarget target,
        CancellationToken cancellationToken)
    {
        var value = WorklogSaveTargetParser.ToConfigValue(target);
        var row = await _db.AppSettings
            .FirstOrDefaultAsync(x => x.Key == WorklogSaveTargetParser.SettingKey, cancellationToken);

        if (row is null)
        {
            _db.AppSettings.Add(new AppSetting
            {
                Key = WorklogSaveTargetParser.SettingKey,
                Value = value,
                UpdatedAt = DateTime.UtcNow,
            });
        }
        else
        {
            row.Value = value;
            row.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(cancellationToken);
        return target;
    }

    public bool IsKimaiBaseUrlConfigured =>
        !string.IsNullOrWhiteSpace(_configuration["Kimai:BaseUrl"])
        && !string.Equals(
            _configuration["Kimai:BaseUrl"]?.Trim(),
            "https://your-kimai-server.com",
            StringComparison.OrdinalIgnoreCase);
}
