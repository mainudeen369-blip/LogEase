using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;

namespace LogEase.Api.Integrations.Kimai;

public sealed class KimaiApiClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<KimaiApiClient> _logger;

    public KimaiApiClient(IHttpClientFactory httpClientFactory, ILogger<KimaiApiClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<IReadOnlyList<KimaiProject>> GetProjectsAsync(
        string baseUrl,
        string apiToken,
        CancellationToken cancellationToken)
    {
        using var document = await GetJsonAsync(baseUrl, apiToken, "/api/projects", cancellationToken);
        return document.RootElement.EnumerateArray()
            .Select(p => new KimaiProject(
                p.GetProperty("id").GetInt32(),
                p.GetProperty("name").GetString() ?? ""))
            .ToList();
    }

    public async Task<IReadOnlyList<KimaiActivity>> GetActivitiesAsync(
        string baseUrl,
        string apiToken,
        int? projectId,
        CancellationToken cancellationToken)
    {
        var path = projectId is null ? "/api/activities" : $"/api/activities?project={projectId.Value}";
        using var document = await GetJsonAsync(baseUrl, apiToken, path, cancellationToken);
        return document.RootElement.EnumerateArray()
            .Select(a =>
            {
                var resolvedProjectId = 0;
                if (a.TryGetProperty("project", out var projectEl))
                {
                    resolvedProjectId = projectEl.ValueKind switch
                    {
                        JsonValueKind.Number => projectEl.GetInt32(),
                        JsonValueKind.Object when projectEl.TryGetProperty("id", out var idEl) => idEl.GetInt32(),
                        _ => 0,
                    };
                }

                return new KimaiActivity(
                    a.GetProperty("id").GetInt32(),
                    a.GetProperty("name").GetString() ?? "",
                    resolvedProjectId);
            })
            .Where(a => projectId is null || a.ProjectId == projectId.Value || a.ProjectId == 0)
            .ToList();
    }

    public async Task<int> CreateTimesheetAsync(
        string baseUrl,
        string apiToken,
        DateOnly workDate,
        TimeOnly beginTime,
        decimal hours,
        int projectId,
        int activityId,
        string description,
        CancellationToken cancellationToken)
    {
        var begin = workDate.ToDateTime(beginTime);
        var end = begin.AddHours((double)hours);
        var payload = new
        {
            begin = begin.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture),
            end = end.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture),
            project = projectId,
            activity = activityId,
            description,
        };

        using var document = await PostJsonAsync(baseUrl, apiToken, "/api/timesheets", payload, cancellationToken);
        return document.RootElement.GetProperty("id").GetInt32();
    }

    public async Task<IReadOnlyList<KimaiTimesheetEntry>> GetTimesheetsAsync(
        string baseUrl,
        string apiToken,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken)
    {
        var begin = from.ToDateTime(TimeOnly.MinValue).ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);
        var end = to.ToDateTime(new TimeOnly(23, 59, 59)).ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);
        var path = $"/api/timesheets?begin={Uri.EscapeDataString(begin)}&end={Uri.EscapeDataString(end)}&size=500&full=true";

        using var document = await GetJsonAsync(baseUrl, apiToken, path, cancellationToken);
        var list = new List<KimaiTimesheetEntry>();
        foreach (var item in document.RootElement.EnumerateArray())
        {
            if (!item.TryGetProperty("begin", out var beginEl)) continue;
            var beginText = beginEl.GetString();
            if (string.IsNullOrWhiteSpace(beginText)) continue;

            if (!DateTime.TryParse(beginText, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var beginAt)
                && !DateTime.TryParse(beginText, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out beginAt))
                continue;

            decimal hours = 0;
            if (item.TryGetProperty("duration", out var durationEl) && durationEl.ValueKind == JsonValueKind.Number)
                hours = Math.Round(durationEl.GetInt32() / 3600m, 2);
            else if (item.TryGetProperty("end", out var endEl))
            {
                var endText = endEl.GetString();
                if (!string.IsNullOrWhiteSpace(endText)
                    && DateTime.TryParse(endText, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var endAt))
                    hours = Math.Round((decimal)(endAt - beginAt).TotalHours, 2);
            }

            list.Add(new KimaiTimesheetEntry(
                item.GetProperty("id").GetInt32(),
                DateOnly.FromDateTime(beginAt),
                hours,
                item.TryGetProperty("description", out var descEl) ? descEl.GetString() ?? "" : "",
                ReadExpandedName(item, "project"),
                ReadExpandedName(item, "activity")));
        }

        return list;
    }

    public async Task<bool> HasTimesheetOnDateAsync(
        string baseUrl,
        string apiToken,
        DateOnly date,
        CancellationToken cancellationToken)
    {
        var entries = await GetTimesheetsAsync(baseUrl, apiToken, date, date, cancellationToken);
        return entries.Count > 0;
    }

    public async Task<KimaiUserProfile?> GetCurrentUserAsync(
        string baseUrl,
        string apiToken,
        CancellationToken cancellationToken)
    {
        using var document = await GetJsonAsync(baseUrl, apiToken, "/api/users/me", cancellationToken);
        var root = document.RootElement;
        var id = root.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.Number
            ? idEl.GetInt32()
            : 0;
        var username = root.TryGetProperty("username", out var userEl) ? userEl.GetString() ?? "" : "";
        var alias = root.TryGetProperty("alias", out var aliasEl) ? aliasEl.GetString() : null;
        var email = root.TryGetProperty("email", out var emailEl) ? emailEl.GetString() : null;
        var title = root.TryGetProperty("title", out var titleEl) ? titleEl.GetString() : null;
        if (string.IsNullOrWhiteSpace(email)
            && root.TryGetProperty("account", out var account)
            && account.ValueKind == JsonValueKind.Object
            && account.TryGetProperty("email", out var accEmail))
        {
            email = accEmail.GetString();
        }

        var display = !string.IsNullOrWhiteSpace(alias)
            ? alias!
            : !string.IsNullOrWhiteSpace(username)
                ? username
                : email ?? $"Kimai user #{id}";

        return new KimaiUserProfile(id, username, alias, email, title, display);
    }

    private static string? ReadExpandedName(JsonElement item, string propertyName)
    {
        if (item.TryGetProperty(propertyName + "Name", out var flatName))
            return flatName.GetString();

        if (!item.TryGetProperty(propertyName, out var nested))
            return null;

        return nested.ValueKind switch
        {
            JsonValueKind.String => nested.GetString(),
            JsonValueKind.Object when nested.TryGetProperty("name", out var nameEl) => nameEl.GetString(),
            _ => null,
        };
    }

    private async Task<JsonDocument> GetJsonAsync(
        string baseUrl,
        string apiToken,
        string path,
        CancellationToken cancellationToken)
    {
        var client = CreateClient(baseUrl, apiToken);
        using var response = await client.GetAsync(NormalizePath(path), cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Kimai GET {Path} failed: {Status} {Body}", path, (int)response.StatusCode, body);
            throw new InvalidOperationException($"Kimai error {(int)response.StatusCode}: {Trim(body, 300)}");
        }

        return JsonDocument.Parse(body);
    }

    private async Task<JsonDocument> PostJsonAsync(
        string baseUrl,
        string apiToken,
        string path,
        object payload,
        CancellationToken cancellationToken)
    {
        var client = CreateClient(baseUrl, apiToken);
        using var response = await client.PostAsJsonAsync(NormalizePath(path), payload, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Kimai POST {Path} failed: {Status} {Body}", path, (int)response.StatusCode, body);
            throw new InvalidOperationException($"Kimai error {(int)response.StatusCode}: {Trim(body, 300)}");
        }

        return JsonDocument.Parse(body);
    }

    private HttpClient CreateClient(string baseUrl, string apiToken)
    {
        var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(60);
        client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiToken);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    private static string NormalizePath(string path) => path.TrimStart('/');

    private static string Trim(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
