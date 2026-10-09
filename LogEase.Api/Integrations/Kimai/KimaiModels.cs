namespace LogEase.Api.Integrations.Kimai;

public sealed record KimaiProject(int Id, string Name);

public sealed record KimaiActivity(int Id, string Name, int ProjectId);

public sealed record KimaiTimesheetEntry(
    int Id,
    DateOnly WorkDate,
    decimal Hours,
    string Description,
    string? ProjectName,
    string? ActivityName);

public sealed record KimaiUserProfile(
    int Id,
    string Username,
    string? Alias,
    string? Email,
    string? Title,
    string DisplayName);

