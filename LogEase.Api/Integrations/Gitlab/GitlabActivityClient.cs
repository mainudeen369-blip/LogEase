using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using LogEase.Api.Entities;

namespace LogEase.Api.Integrations.Gitlab;

public sealed class GitlabActivityClient
{
    private readonly IHttpClientFactory _httpFactory;

    public GitlabActivityClient(IHttpClientFactory httpFactory) => _httpFactory = httpFactory;

    public static string NormalizeBaseUrl(string? url)
    {
        var u = string.IsNullOrWhiteSpace(url) ? "https://gitlab.com" : url.Trim();
        u = u.TrimEnd('/');
        if (u.EndsWith("/api/v4", StringComparison.OrdinalIgnoreCase))
            u = u[..^7];
        return u;
    }

    public static string NormalizeToken(string token)
    {
        var t = token.Trim();
        if (t.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            t = t[7..].Trim();
        return t;
    }

    public async Task<string> FetchActivityDigestAsync(
        User user,
        DateOnly from,
        DateOnly to,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(user.GitlabPersonalAccessToken))
            return "";

        var baseUrl = NormalizeBaseUrl(user.GitlabBaseUrl);
        var token = NormalizeToken(user.GitlabPersonalAccessToken);

        // GitLab events API: after is exclusive; before is exclusive — pad by 1 day.
        var after = from.AddDays(-1).ToString("yyyy-MM-dd");
        var before = to.AddDays(1).ToString("yyyy-MM-dd");
        var path =
            $"/api/v4/events?after={Uri.EscapeDataString(after)}&before={Uri.EscapeDataString(before)}&per_page=50";

        using var resp = await GetGitlabAsync(baseUrl, token, path, TimeSpan.FromSeconds(45), ct);
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException(
                $"GitLab events failed ({(int)resp.StatusCode}) at {baseUrl}{path}: {Truncate(body, 200)}");
        }

        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
            return "";

        var lines = new List<string>();
        foreach (var ev in doc.RootElement.EnumerateArray())
        {
            var action = ev.TryGetProperty("action_name", out var a) ? a.GetString() : null;
            var created = ev.TryGetProperty("created_at", out var c) ? c.GetString() : null;
            var target = ev.TryGetProperty("target_title", out var t) ? t.GetString() : null;
            var project = "";
            if (ev.TryGetProperty("project_id", out var pid) && pid.ValueKind == JsonValueKind.Number)
                project = $"project#{pid.GetInt32()}";

            var pushTitle = "";
            if (ev.TryGetProperty("push_data", out var push) && push.ValueKind == JsonValueKind.Object)
            {
                if (push.TryGetProperty("commit_title", out var ctTitle))
                    pushTitle = ctTitle.GetString() ?? "";
            }

            var day = created is not null && DateTime.TryParse(created, out var dt)
                ? dt.ToString("yyyy-MM-dd")
                : "?";

            var detail = !string.IsNullOrWhiteSpace(pushTitle)
                ? pushTitle
                : (target ?? action ?? "activity");
            lines.Add($"{day}: GitLab {action ?? "event"} — {detail} ({project})");
            if (lines.Count >= 40) break;
        }

        if (lines.Count == 0)
            return "(GitLab: no events in this period.)";

        var sb = new StringBuilder();
        sb.AppendLine("## GitLab activity");
        foreach (var line in lines)
            sb.AppendLine(line);
        return sb.ToString();
    }

    /// <summary>Validate PAT by calling GET /api/v4/user. Returns display name or null on failure.</summary>
    public async Task<string?> ValidateTokenAsync(User user, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(user.GitlabPersonalAccessToken))
            return null;

        var baseUrl = NormalizeBaseUrl(user.GitlabBaseUrl);
        var token = NormalizeToken(user.GitlabPersonalAccessToken);
        const string path = "/api/v4/user";

        using var resp = await GetGitlabAsync(baseUrl, token, path, TimeSpan.FromSeconds(20), ct);
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException(
                BuildAuthErrorMessage((int)resp.StatusCode, baseUrl, path, body));
        }

        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var root = doc.RootElement;
        var name = root.TryGetProperty("name", out var n) ? n.GetString() : null;
        var username = root.TryGetProperty("username", out var u) ? u.GetString() : null;
        if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(username))
            return $"{name} (@{username})";
        return name ?? username ?? "GitLab user";
    }

    private async Task<HttpResponseMessage> GetGitlabAsync(
        string baseUrl,
        string token,
        string path,
        TimeSpan timeout,
        CancellationToken ct)
    {
        var http = _httpFactory.CreateClient();
        http.Timeout = timeout;

        using var privateTokenReq = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}{path}");
        privateTokenReq.Headers.TryAddWithoutValidation("PRIVATE-TOKEN", token);
        privateTokenReq.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var resp = await http.SendAsync(privateTokenReq, ct);
        if (resp.IsSuccessStatusCode || resp.StatusCode != HttpStatusCode.Unauthorized)
            return resp;

        resp.Dispose();

        using var bearerReq = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}{path}");
        bearerReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        bearerReq.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return await http.SendAsync(bearerReq, ct);
    }

    private static string BuildAuthErrorMessage(int statusCode, string baseUrl, string path, string body) =>
        statusCode switch
        {
            401 =>
                $"GitLab rejected this token (401) at {baseUrl}{path}. " +
                "Check: (1) base URL matches where the token was created — use your company GitLab host, not gitlab.com, if self-hosted; " +
                "(2) token is a personal access token (glpat-…), not a project/group/deploy token; " +
                "(3) token is not expired or revoked; " +
                "(4) scopes include read_api or read_user. " +
                $"Response: {Truncate(body, 80)}",
            403 =>
                $"GitLab denied access (403) at {baseUrl}{path}. Add read_api or read_user scope to the token. Response: {Truncate(body, 80)}",
            _ =>
                $"GitLab request failed ({statusCode}) at {baseUrl}{path}: {Truncate(body, 120)}",
        };

    private static string Truncate(string s, int n) =>
        string.IsNullOrEmpty(s) ? "" : s.Length <= n ? s : s[..n] + "…";
}
