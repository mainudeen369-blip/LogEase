using System.ComponentModel;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace LogEase.Desktop;

public sealed class LogEaseApiClient
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(120) };

    public async Task<LoginResponse> LoginWithEmailAsync(string apiUrl, string email, string password) =>
        await LoginInternalAsync(apiUrl, """
            mutation ($input: LoginInput!) {
              login(input: $input) {
                success token error name email profilePhotoUrl hasKimaiToken
              }
            }
            """, new { input = new { email, password } });

    public async Task<LoginResponse> LoginWithAzureAsync(string apiUrl, string idToken, string? accessToken = null) =>
        await LoginInternalAsync(apiUrl, """
            mutation ($input: AzureLoginInput!) {
              loginWithAzure(input: $input) {
                success token error name email profilePhotoUrl hasKimaiToken
              }
            }
            """, new { input = new { idToken, accessToken } });

    public async Task<(bool Ok, string? Token, string? Error)> LoginAsync(string apiUrl, string email, string password)
    {
        var result = await LoginWithEmailAsync(apiUrl, email, password);
        return result.Success
            ? (true, result.Token, null)
            : (false, null, result.Error);
    }

    public async Task<AuthConfig?> GetAuthConfigAsync(string apiUrl)
    {
        var data = await PostAsync(apiUrl, null,
            """
            query {
              authConfig {
                azureEnabled tenantId clientId instance scopes
              }
            }
            """,
            null);
        if (data is null) return null;
        var cfg = data.RootElement.GetProperty("authConfig");
        var scopes = new List<string>();
        if (cfg.TryGetProperty("scopes", out var arr))
        {
            foreach (var s in arr.EnumerateArray())
            {
                var v = s.GetString();
                if (!string.IsNullOrWhiteSpace(v)) scopes.Add(v);
            }
        }

        return new AuthConfig
        {
            AzureEnabled = cfg.TryGetProperty("azureEnabled", out var en) && en.GetBoolean(),
            TenantId = cfg.TryGetProperty("tenantId", out var t) && t.ValueKind == JsonValueKind.String
                ? t.GetString()
                : null,
            ClientId = cfg.TryGetProperty("clientId", out var c) && c.ValueKind == JsonValueKind.String
                ? c.GetString()
                : null,
            Instance = cfg.TryGetProperty("instance", out var i) ? i.GetString() ?? "https://login.microsoftonline.com/" : "https://login.microsoftonline.com/",
            Scopes = scopes,
        };
    }

    private async Task<LoginResponse> LoginInternalAsync(string apiUrl, string query, object? variables)
    {
        var data = await PostAsync(apiUrl, null, query, variables);
        if (data is null)
            return new LoginResponse { Success = false, Error = "No response" };

        var root = data.RootElement;
        var login = root.TryGetProperty("login", out var l) ? l : root.GetProperty("loginWithAzure");
        return ParseLoginResponse(login);
    }

    private static LoginResponse ParseLoginResponse(JsonElement login)
    {
        var success = login.TryGetProperty("success", out var s) && s.GetBoolean();
        if (!success)
        {
            return new LoginResponse
            {
                Success = false,
                Error = login.TryGetProperty("error", out var e) ? e.GetString() : "Login failed",
            };
        }

        return new LoginResponse
        {
            Success = true,
            Token = login.TryGetProperty("token", out var t) ? t.GetString() : null,
            Name = login.TryGetProperty("name", out var n) ? n.GetString() : null,
            Email = login.TryGetProperty("email", out var em) ? em.GetString() : null,
            ProfilePhotoUrl = login.TryGetProperty("profilePhotoUrl", out var p) && p.ValueKind == JsonValueKind.String
                ? p.GetString()
                : null,
            HasKimaiToken = login.TryGetProperty("hasKimaiToken", out var h) && h.GetBoolean(),
        };
    }

    public async Task<List<ParsedRow>> ParseAsync(string apiUrl, string token, string text, string dateFrom, string dateTo)
    {
        var data = await PostAsync(apiUrl, token,
            """
            mutation ($input: ParseWorklogInput!) {
              parseWorklog(input: $input) {
                rows {
                  workDate projectName activityName hours description confidence projectId activityId
                }
              }
            }
            """,
            new { input = new { text, dateFrom, dateTo } });

        var rows = new List<ParsedRow>();
        if (data is null) return rows;
        foreach (var r in data.RootElement.GetProperty("parseWorklog").GetProperty("rows").EnumerateArray())
        {
            rows.Add(new ParsedRow
            {
                WorkDate = r.GetProperty("workDate").GetString() ?? "",
                ProjectName = r.GetProperty("projectName").GetString() ?? "",
                ActivityName = r.GetProperty("activityName").GetString() ?? "",
                Hours = r.GetProperty("hours").GetDecimal(),
                Description = r.GetProperty("description").GetString() ?? "",
                Confidence = r.TryGetProperty("confidence", out var c) ? c.GetString() ?? "" : "",
                ProjectId = r.TryGetProperty("projectId", out var p) && p.ValueKind != JsonValueKind.Null ? p.GetGuid() : null,
                ActivityId = r.TryGetProperty("activityId", out var a) && a.ValueKind != JsonValueKind.Null ? a.GetGuid() : null,
            });
        }
        return rows;
    }

    public async Task<List<ProjectInfo>> GetProjectsAsync(string apiUrl, string token)
    {
        var data = await PostAsync(apiUrl, token,
            """
            query {
              projects {
                id name
                activities { id name projectId }
              }
            }
            """,
            null);

        var list = new List<ProjectInfo>();
        if (data is null) return list;
        foreach (var p in data.RootElement.GetProperty("projects").EnumerateArray())
        {
            var acts = new List<ActivityInfo>();
            if (p.TryGetProperty("activities", out var arr))
            {
                foreach (var a in arr.EnumerateArray())
                {
                    acts.Add(new ActivityInfo
                    {
                        Id = a.GetProperty("id").GetGuid(),
                        Name = a.GetProperty("name").GetString() ?? "",
                        ProjectId = a.GetProperty("projectId").GetGuid(),
                    });
                }
            }
            list.Add(new ProjectInfo
            {
                Id = p.GetProperty("id").GetGuid(),
                Name = p.GetProperty("name").GetString() ?? "",
                Activities = acts,
            });
        }
        return list;
    }

    public async Task<int> SaveAsync(string apiUrl, string token, IEnumerable<ParsedRow> rows)
    {
        var items = rows
            .Where(r => r.ProjectId is not null && r.ActivityId is not null)
            .Select(r => new
            {
                workDate = r.WorkDate,
                projectId = r.ProjectId,
                activityId = r.ActivityId,
                hours = r.Hours,
                description = r.Description,
            })
            .ToList();

        var data = await PostAsync(apiUrl, token,
            """
            mutation ($input: SaveWorklogsInput!) {
              saveWorklogs(input: $input) { savedCount }
            }
            """,
            new { input = new { items } });

        return data?.RootElement.GetProperty("saveWorklogs").GetProperty("savedCount").GetInt32() ?? 0;
    }

    public async Task<List<StickyNoteInfo>> GetStickyNotesAsync(string apiUrl, string token, string from, string to)
    {
        var data = await PostAsync(apiUrl, token,
            """
            query ($from: String, $to: String) {
              stickyNotes(from: $from, to: $to) { id noteDate body }
            }
            """,
            new { from, to });
        var list = new List<StickyNoteInfo>();
        if (data is null) return list;
        foreach (var n in data.RootElement.GetProperty("stickyNotes").EnumerateArray())
        {
            list.Add(new StickyNoteInfo
            {
                Id = n.GetProperty("id").GetGuid(),
                NoteDate = n.GetProperty("noteDate").GetString() ?? "",
                Body = n.GetProperty("body").GetString() ?? "",
            });
        }
        return list;
    }

    public async Task CreateStickyNoteAsync(string apiUrl, string token, string noteDate, string body)
    {
        await PostAsync(apiUrl, token,
            """
            mutation ($input: CreateStickyNoteInput!) {
              createStickyNote(input: $input) { id }
            }
            """,
            new { input = new { noteDate, body } });
    }

    public async Task UpdateStickyNoteAsync(string apiUrl, string token, Guid id, string noteDate, string body)
    {
        await PostAsync(apiUrl, token,
            """
            mutation ($input: UpdateStickyNoteInput!) {
              updateStickyNote(input: $input) { id noteDate body }
            }
            """,
            new { input = new { id, noteDate, body } });
    }

    public async Task DeleteStickyNoteAsync(string apiUrl, string token, Guid id)
    {
        await PostAsync(apiUrl, token,
            "mutation ($id: UUID!) { deleteStickyNote(id: $id) }",
            new { id });
    }

    public async Task<(List<ParsedRow> Rows, string Parser, string SourcesUsed, string? Warning)> DraftFromSourcesAsync(
        string apiUrl,
        string token,
        string dateFrom,
        string dateTo,
        bool includeNotes,
        bool includeGitlab,
        bool includeMail)
    {
        var data = await PostAsync(apiUrl, token,
            """
            mutation ($input: DraftFromSourcesInput!) {
              draftWorklogFromSources(input: $input) {
                parser warning sourcesUsed
                rows {
                  workDate projectName activityName hours description confidence projectId activityId
                }
              }
            }
            """,
            new
            {
                input = new
                {
                    dateFrom,
                    dateTo,
                    includeNotes,
                    includeGitlab,
                    includeMail,
                },
            });

        var rows = new List<ParsedRow>();
        if (data is null) return (rows, "", "", null);
        var payload = data.RootElement.GetProperty("draftWorklogFromSources");
        foreach (var r in payload.GetProperty("rows").EnumerateArray())
        {
            rows.Add(new ParsedRow
            {
                WorkDate = r.GetProperty("workDate").GetString() ?? "",
                ProjectName = r.GetProperty("projectName").GetString() ?? "",
                ActivityName = r.GetProperty("activityName").GetString() ?? "",
                Hours = r.GetProperty("hours").GetDecimal(),
                Description = r.GetProperty("description").GetString() ?? "",
                Confidence = r.TryGetProperty("confidence", out var c) ? c.GetString() ?? "" : "",
                ProjectId = r.TryGetProperty("projectId", out var p) && p.ValueKind != JsonValueKind.Null ? p.GetGuid() : null,
                ActivityId = r.TryGetProperty("activityId", out var a) && a.ValueKind != JsonValueKind.Null ? a.GetGuid() : null,
            });
        }

        return (
            rows,
            payload.TryGetProperty("parser", out var pr) ? pr.GetString() ?? "" : "",
            payload.TryGetProperty("sourcesUsed", out var su) ? su.GetString() ?? "" : "",
            payload.TryGetProperty("warning", out var w) && w.ValueKind == JsonValueKind.String ? w.GetString() : null);
    }

    public async Task<(bool ShouldRemind, string Message)> ReminderStatusAsync(string apiUrl, string token)
    {
        var data = await PostAsync(apiUrl, token,
            "query { reminderStatus { shouldRemind message reminderTime enabled } }",
            null);
        if (data is null) return (false, "");
        var s = data.RootElement.GetProperty("reminderStatus");
        return (s.GetProperty("shouldRemind").GetBoolean(), s.GetProperty("message").GetString() ?? "");
    }

    private const string MeFields = """
        email name profilePhotoUrl
        reminderEnabled reminderTime hasKimaiToken kimaiError aiParseInstructions
        kimaiUser { id username alias email title displayName }
        hasGitlabToken gitlabBaseUrl gitlabUsername gitlabConnectedUser gitlabError
        hasMailSettings mailImapHost mailImapPort mailImapUser mailImapUseSsl
        """;

    public async Task<UserSettings?> MeAsync(string apiUrl, string token)
    {
        var data = await PostAsync(apiUrl, token,
            "query { me { " + MeFields.Trim() + " } }",
            null);
        if (data is null) return null;
        var me = data.RootElement.GetProperty("me");
        return ParseUserSettings(me);
    }

    public async Task<string?> GetDefaultAiParseInstructionsAsync(string apiUrl, string? token = null)
    {
        var data = await PostAsync(apiUrl, token,
            "query { defaultAiParseInstructions }",
            null);
        return data?.RootElement.GetProperty("defaultAiParseInstructions").GetString();
    }

    public async Task UpdateAiParseInstructionsAsync(string apiUrl, string token, string? instructions)
    {
        await PostAsync(apiUrl, token,
            """
            mutation ($input: UpdateAiParseInstructionsInput!) {
              updateAiParseInstructions(input: $input) { aiParseInstructions }
            }
            """,
            new { input = new { instructions } });
    }

    public async Task UpdateReminderAsync(string apiUrl, string token, bool enabled, string reminderTime)
    {
        await PostAsync(apiUrl, token,
            """
            mutation ($input: UpdateReminderInput!) {
              updateReminder(input: $input) { reminderEnabled reminderTime }
            }
            """,
            new { input = new { enabled, reminderTime } });
    }

    public async Task<UserSettings?> UpdateKimaiSettingsAsync(string apiUrl, string token, string? apiToken)
    {
        var data = await PostAsync(apiUrl, token,
            "mutation ($input: UpdateKimaiSettingsInput!) { updateKimaiSettings(input: $input) { "
            + MeFields.Trim()
            + " } }",
            new { input = new { apiToken } });
        if (data is null) return null;
        return ParseUserSettings(data.RootElement.GetProperty("updateKimaiSettings"));
    }

    public async Task<UserSettings?> UpdateGitlabSettingsAsync(
        string apiUrl,
        string token,
        string? personalAccessToken,
        string? baseUrl,
        string? username)
    {
        var data = await PostAsync(apiUrl, token,
            "mutation ($input: UpdateGitlabSettingsInput!) { updateGitlabSettings(input: $input) { "
            + MeFields.Trim()
            + " } }",
            new
            {
                input = new
                {
                    personalAccessToken,
                    baseUrl,
                    username,
                },
            });
        if (data is null) return null;
        return ParseUserSettings(data.RootElement.GetProperty("updateGitlabSettings"));
    }

    public async Task<UserSettings?> UpdateMailSettingsAsync(
        string apiUrl,
        string token,
        string? imapHost,
        int? imapPort,
        string? imapUser,
        string? imapPassword,
        bool? useSsl)
    {
        var data = await PostAsync(apiUrl, token,
            "mutation ($input: UpdateMailSettingsInput!) { updateMailSettings(input: $input) { "
            + MeFields.Trim()
            + " } }",
            new
            {
                input = new
                {
                    imapHost,
                    imapPort,
                    imapUser,
                    imapPassword,
                    useSsl,
                },
            });
        if (data is null) return null;
        return ParseUserSettings(data.RootElement.GetProperty("updateMailSettings"));
    }

    private static UserSettings ParseUserSettings(JsonElement me)
    {
        KimaiUserInfo? kimai = null;
        if (me.TryGetProperty("kimaiUser", out var ku) && ku.ValueKind == JsonValueKind.Object)
        {
            kimai = new KimaiUserInfo
            {
                Id = ku.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number ? id.GetInt32() : 0,
                Username = ku.TryGetProperty("username", out var un) ? un.GetString() ?? "" : "",
                Alias = ku.TryGetProperty("alias", out var al) ? al.GetString() : null,
                Email = ku.TryGetProperty("email", out var em) ? em.GetString() : null,
                Title = ku.TryGetProperty("title", out var ti) ? ti.GetString() : null,
                DisplayName = ku.TryGetProperty("displayName", out var dn) ? dn.GetString() ?? "" : "",
            };
        }

        return new UserSettings
        {
            Name = me.TryGetProperty("name", out var nm) ? nm.GetString() : null,
            Email = me.TryGetProperty("email", out var eml) ? eml.GetString() : null,
            ProfilePhotoUrl = me.TryGetProperty("profilePhotoUrl", out var ph) && ph.ValueKind == JsonValueKind.String
                ? ph.GetString()
                : null,
            ReminderEnabled = me.TryGetProperty("reminderEnabled", out var re) && re.GetBoolean(),
            ReminderTime = me.TryGetProperty("reminderTime", out var rt) ? rt.GetString() ?? "17:45" : "17:45",
            HasKimaiToken = me.TryGetProperty("hasKimaiToken", out var ht) && ht.GetBoolean(),
            KimaiError = me.TryGetProperty("kimaiError", out var ke) && ke.ValueKind != JsonValueKind.Null
                ? ke.GetString()
                : null,
            AiParseInstructions = me.TryGetProperty("aiParseInstructions", out var ai)
                && ai.ValueKind == JsonValueKind.String
                ? ai.GetString()
                : null,
            KimaiUser = kimai,
            HasGitlabToken = me.TryGetProperty("hasGitlabToken", out var hgt) && hgt.GetBoolean(),
            GitlabBaseUrl = me.TryGetProperty("gitlabBaseUrl", out var gbu) && gbu.ValueKind == JsonValueKind.String
                ? gbu.GetString()
                : null,
            GitlabUsername = me.TryGetProperty("gitlabUsername", out var gun) && gun.ValueKind == JsonValueKind.String
                ? gun.GetString()
                : null,
            GitlabConnectedUser = me.TryGetProperty("gitlabConnectedUser", out var gcu) && gcu.ValueKind == JsonValueKind.String
                ? gcu.GetString()
                : null,
            GitlabError = me.TryGetProperty("gitlabError", out var ge) && ge.ValueKind == JsonValueKind.String
                ? ge.GetString()
                : null,
            HasMailSettings = me.TryGetProperty("hasMailSettings", out var hms) && hms.GetBoolean(),
            MailImapHost = me.TryGetProperty("mailImapHost", out var mih) && mih.ValueKind == JsonValueKind.String
                ? mih.GetString()
                : null,
            MailImapPort = me.TryGetProperty("mailImapPort", out var mip) && mip.ValueKind == JsonValueKind.Number
                ? mip.GetInt32()
                : null,
            MailImapUser = me.TryGetProperty("mailImapUser", out var miu) && miu.ValueKind == JsonValueKind.String
                ? miu.GetString()
                : null,
            MailImapUseSsl = !me.TryGetProperty("mailImapUseSsl", out var mis) || mis.GetBoolean(),
        };
    }

    public async Task<List<WorklogInfo>> GetWorklogsAsync(
        string apiUrl,
        string token,
        string from,
        string to,
        string? source = null)
    {
        var data = await PostAsync(apiUrl, token,
            """
            query ($from: String, $to: String, $source: String) {
              worklogs(from: $from, to: $to, source: $source) {
                id workDate projectName hours description
              }
            }
            """,
            new { from, to, source });

        var list = new List<WorklogInfo>();
        if (data is null) return list;
        foreach (var w in data.RootElement.GetProperty("worklogs").EnumerateArray())
        {
            list.Add(new WorklogInfo
            {
                Id = w.GetProperty("id").GetString() ?? "",
                WorkDate = w.GetProperty("workDate").GetString() ?? "",
                ProjectName = w.TryGetProperty("projectName", out var p) ? p.GetString() ?? "" : "",
                Hours = w.TryGetProperty("hours", out var h) && h.ValueKind == JsonValueKind.Number ? h.GetDecimal() : 0,
                Description = w.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "",
            });
        }
        return list;
    }

    private async Task<JsonDocument?> PostAsync(string apiUrl, string? token, string query, object? variables)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, apiUrl);
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            if (!string.IsNullOrWhiteSpace(token))
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var payload = JsonSerializer.Serialize(new { query, variables });
            req.Content = new StringContent(payload, Encoding.UTF8, "application/json");

            using var resp = await _http.SendAsync(req);
            var body = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("errors", out var errors) && errors.GetArrayLength() > 0)
            {
                var err = errors[0];
                var msg = err.GetProperty("message").GetString() ?? "GraphQL error";
                // HotChocolate often hides the real exception as "Unexpected Execution Error".
                // Prefer message from extensions.exception / extensions.message when present.
                if (err.TryGetProperty("extensions", out var ext))
                {
                    if (ext.TryGetProperty("message", out var em) && em.ValueKind == JsonValueKind.String)
                        msg = em.GetString() ?? msg;
                    else if (ext.TryGetProperty("exception", out var exObj))
                    {
                        if (exObj.ValueKind == JsonValueKind.Object
                            && exObj.TryGetProperty("message", out var exMsg)
                            && exMsg.ValueKind == JsonValueKind.String)
                            msg = exMsg.GetString() ?? msg;
                        else if (exObj.ValueKind == JsonValueKind.String)
                            msg = exObj.GetString() ?? msg;
                    }
                }

                throw new InvalidOperationException("LogEase API error: " + msg + "\n\nRaw: " + body);
            }
            if (!doc.RootElement.TryGetProperty("data", out var data)) return null;
            return JsonDocument.Parse(data.GetRawText());
        }
        catch (Exception ex) when (IsApiUnreachable(ex))
        {
            throw new InvalidOperationException(
                "Cannot reach LogEase API.\n\n" +
                "Start the API first:\n" +
                "  cd LogEase.Api\n" +
                "  dotnet run\n\n" +
                $"Expected URL: {apiUrl}\n" +
                "(Check tray → Settings if you use a different server.)",
                ex);
        }
    }

    private static bool IsApiUnreachable(Exception ex) =>
        ex is HttpRequestException or SocketException
        || (ex.InnerException is not null && IsApiUnreachable(ex.InnerException));
}

public sealed class ParsedRow : INotifyPropertyChanged
{
    private string _workDate = "";
    private string _projectName = "";
    private string _activityName = "";
    private decimal _hours;
    private string _description = "";
    private string _confidence = "";
    private Guid? _projectId;
    private Guid? _activityId;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string WorkDate
    {
        get => _workDate;
        set => Set(ref _workDate, value);
    }
    public string ProjectName
    {
        get => _projectName;
        set => Set(ref _projectName, value);
    }
    public string ActivityName
    {
        get => _activityName;
        set => Set(ref _activityName, value);
    }
    public decimal Hours
    {
        get => _hours;
        set => Set(ref _hours, value);
    }
    public string Description
    {
        get => _description;
        set => Set(ref _description, value);
    }
    public string Confidence
    {
        get => _confidence;
        set => Set(ref _confidence, value);
    }
    public Guid? ProjectId
    {
        get => _projectId;
        set => Set(ref _projectId, value);
    }
    public Guid? ActivityId
    {
        get => _activityId;
        set => Set(ref _activityId, value);
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

public sealed class ProjectInfo
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public List<ActivityInfo> Activities { get; set; } = new();
}

public sealed class ActivityInfo
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public Guid ProjectId { get; set; }
}

public sealed class UserSettings
{
    public string? Name { get; set; }
    public string? Email { get; set; }
    public string? ProfilePhotoUrl { get; set; }
    public bool ReminderEnabled { get; set; }
    public string ReminderTime { get; set; } = "17:45";
    public bool HasKimaiToken { get; set; }
    public string? KimaiError { get; set; }
    public string? AiParseInstructions { get; set; }
    public KimaiUserInfo? KimaiUser { get; set; }
    public bool HasGitlabToken { get; set; }
    public string? GitlabBaseUrl { get; set; }
    public string? GitlabUsername { get; set; }
    public string? GitlabConnectedUser { get; set; }
    public string? GitlabError { get; set; }
    public bool HasMailSettings { get; set; }
    public string? MailImapHost { get; set; }
    public int? MailImapPort { get; set; }
    public string? MailImapUser { get; set; }
    public bool MailImapUseSsl { get; set; } = true;
}

public sealed class KimaiUserInfo
{
    public int Id { get; set; }
    public string Username { get; set; } = "";
    public string? Alias { get; set; }
    public string? Email { get; set; }
    public string? Title { get; set; }
    public string DisplayName { get; set; } = "";
}

public sealed class WorklogInfo
{
    public string Id { get; set; } = "";
    public string WorkDate { get; set; } = "";
    public string ProjectName { get; set; } = "";
    public decimal Hours { get; set; }
    public string Description { get; set; } = "";
}

public sealed class StickyNoteInfo : INotifyPropertyChanged
{
    private string _noteDate = "";
    private string _body = "";

    public event PropertyChangedEventHandler? PropertyChanged;

    public Guid Id { get; set; }

    public string NoteDate
    {
        get => _noteDate;
        set
        {
            if (_noteDate == value) return;
            _noteDate = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(NoteDate)));
        }
    }

    public string Body
    {
        get => _body;
        set
        {
            if (_body == value) return;
            _body = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Body)));
        }
    }
}
