namespace LogEase.Api.Integrations.Kimai;

public interface IKimaiTimesheetAdapter
{
    Task<KimaiSyncResult> CreateTimesheetsAsync(
        string apiToken,
        IReadOnlyList<KimaiTimesheetDraft> drafts,
        CancellationToken cancellationToken = default);
}

public sealed record KimaiTimesheetDraft(
    DateOnly WorkDate,
    decimal Hours,
    string Description,
    string? ProjectName,
    string? ActivityName,
    int? KimaiProjectId = null,
    int? KimaiActivityId = null);

public sealed record KimaiSyncResult(bool Success, string Message, int CreatedCount = 0);

public sealed class KimaiTimesheetAdapter : IKimaiTimesheetAdapter
{
    private readonly KimaiTimesheetService _service;

    public KimaiTimesheetAdapter(KimaiTimesheetService service) => _service = service;

    public Task<KimaiSyncResult> CreateTimesheetsAsync(
        string apiToken,
        IReadOnlyList<KimaiTimesheetDraft> drafts,
        CancellationToken cancellationToken = default)
        => _service.CreateTimesheetsAsync(apiToken, drafts, cancellationToken);
}
