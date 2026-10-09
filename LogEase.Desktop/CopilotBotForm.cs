namespace LogEase.Desktop;

/// <summary>Chat-style work copilot — summarize, parse, structured rows (web bot equivalent).</summary>
public sealed class CopilotBotForm : Form
{
    private readonly AppSettings _settings;
    private readonly LogEaseApiClient _api;
    private readonly RichTextBox _chat = new()
    {
        ReadOnly = true,
        BackColor = Color.White,
        BorderStyle = BorderStyle.None,
        ScrollBars = RichTextBoxScrollBars.Vertical,
        Dock = DockStyle.Fill,
        Font = UiTheme.Ui,
    };
    private readonly TextBox _input = new() { Dock = DockStyle.Fill };
    private readonly DataGridView _grid = new()
    {
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        ReadOnly = true,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        Height = 160,
        Dock = DockStyle.Bottom,
        Visible = false,
    };
    private readonly Button _save = new() { Text = "Save rows", AutoSize = true, Visible = false };
    private List<ParsedRow> _lastRows = new();

    private static readonly string[] QuickPrompts =
    [
        "Summarize this week from my sticky notes",
        "What work did I already log this week?",
        "Summarize from GitLab and notes",
        "Summarize from mail and notes",
    ];

    public CopilotBotForm(AppSettings settings, LogEaseApiClient api)
    {
        _settings = settings;
        _api = api;
        Text = "LogEase — Work copilot";
        Width = 520;
        Height = 640;
        MinimumSize = new Size(420, 480);
        StartPosition = FormStartPosition.CenterScreen;
        Font = UiTheme.Ui;
        BackColor = UiTheme.Bg;

        var header = new Label
        {
            Dock = DockStyle.Top,
            Height = 48,
            Text = "Work copilot — ask, summarize, get structured rows",
            Padding = new Padding(12, 12, 12, 0),
            ForeColor = UiTheme.Muted,
            Font = UiTheme.Hint,
        };

        var quick = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 72,
            Padding = new Padding(8, 4, 8, 4),
            AutoScroll = true,
            WrapContents = true,
        };
        foreach (var q in QuickPrompts)
        {
            var chip = UiTheme.SecondaryButton(q);
            chip.Margin = new Padding(0, 0, 6, 6);
            chip.Click += async (_, _) => await SendAsync(q);
            quick.Controls.Add(chip);
        }

        _grid.Columns.Add("workDate", "Date");
        _grid.Columns.Add("project", "Project");
        _grid.Columns.Add("activity", "Activity");
        _grid.Columns.Add("hours", "Hours");
        _grid.Columns.Add("desc", "Description");

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 52, Padding = new Padding(8) };
        var sendBtn = UiTheme.PrimaryButton("Send");
        sendBtn.Dock = DockStyle.Right;
        sendBtn.Click += async (_, _) => await SendAsync(_input.Text);
        _input.KeyDown += async (_, e) =>
        {
            if (e.KeyCode == Keys.Enter && !e.Shift)
            {
                e.SuppressKeyPress = true;
                await SendAsync(_input.Text);
            }
        };
        bottom.Controls.Add(sendBtn);
        bottom.Controls.Add(_input);

        var saveBar = new Panel { Dock = DockStyle.Bottom, Height = 40, Padding = new Padding(8, 4, 8, 8) };
        _save.Click += async (_, _) => await SaveRowsAsync();
        saveBar.Controls.Add(_save);

        var chatHost = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(8),
            BackColor = UiTheme.Surface,
        };
        chatHost.Controls.Add(_chat);

        Controls.Add(chatHost);
        Controls.Add(_grid);
        Controls.Add(saveBar);
        Controls.Add(bottom);
        Controls.Add(quick);
        Controls.Add(header);

        AppendBot(
            "Hi — ask what you worked on, summarize sticky notes / GitLab / mail, or paste messy text. " +
            "I'll show structured worklog rows you can save.");
    }

    private void AppendUser(string text)
    {
        _chat.SelectionColor = UiTheme.Brand;
        _chat.AppendText("You: " + text.Trim() + Environment.NewLine);
        _chat.SelectionColor = UiTheme.Ink;
    }

    private void AppendBot(string text)
    {
        _chat.SelectionColor = UiTheme.Muted;
        _chat.AppendText("Copilot: " + text.Trim() + Environment.NewLine + Environment.NewLine);
        _chat.SelectionColor = UiTheme.Ink;
        _chat.ScrollToCaret();
    }

    private async Task SendAsync(string raw)
    {
        var text = raw.Trim();
        if (string.IsNullOrEmpty(text)) return;
        if (string.IsNullOrWhiteSpace(_settings.Token))
        {
            MessageBox.Show(this, "Sign in under Settings first.", "LogEase", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        _input.Clear();
        AppendUser(text);
        try
        {
            var from = DateTime.Today.AddDays(-6).ToString("yyyy-MM-dd");
            var to = DateTime.Today.ToString("yyyy-MM-dd");
            var intent = CopilotIntent.Detect(text);

            if (intent == CopilotIntent.Kind.History)
            {
                var logs = await _api.GetWorklogsAsync(_settings.ApiUrl, _settings.Token!, from, to);
                if (logs.Count == 0)
                {
                    AppendBot($"No saved worklogs between {from} and {to}. Try summarizing sticky notes.");
                    HideGrid();
                    return;
                }
                var lines = string.Join(Environment.NewLine,
                    logs.Select(w => $"{w.WorkDate} · {w.ProjectName} · {w.Hours}h — {w.Description}"));
                AppendBot($"Found {logs.Count} saved row(s):{Environment.NewLine}{lines}");
                HideGrid();
                return;
            }

            if (intent != CopilotIntent.Kind.Parse)
            {
                var includeNotes = intent is CopilotIntent.Kind.SourcesAll or CopilotIntent.Kind.SourcesNotes;
                var includeGitlab = intent is CopilotIntent.Kind.SourcesAll or CopilotIntent.Kind.SourcesGitlab;
                var includeMail = intent is CopilotIntent.Kind.SourcesAll or CopilotIntent.Kind.SourcesMail;
                var result = await _api.DraftFromSourcesAsync(
                    _settings.ApiUrl, _settings.Token!, from, to, includeNotes, includeGitlab, includeMail);
                _lastRows = result.Rows;
                AppendBot(
                    $"Draft via {result.Parser}. Sources: {result.SourcesUsed}." +
                    (string.IsNullOrWhiteSpace(result.Warning) ? "" : $" Note: {result.Warning}") +
                    Environment.NewLine + "Structured rows below — review then Save.");
                ShowGrid(_lastRows);
                return;
            }

            var parsed = await _api.ParseAsync(_settings.ApiUrl, _settings.Token!, text, from, to);
            _lastRows = parsed;
            AppendBot($"Parsed your text into {parsed.Count} row(s). Structured output below.");
            ShowGrid(_lastRows);
        }
        catch (Exception ex)
        {
            AppendBot("Error: " + ex.Message);
        }
    }

    private void ShowGrid(List<ParsedRow> rows)
    {
        _grid.Rows.Clear();
        foreach (var r in rows)
            _grid.Rows.Add(r.WorkDate, r.ProjectName, r.ActivityName, r.Hours, r.Description);
        _grid.Visible = rows.Count > 0;
        _save.Visible = rows.Count > 0;
    }

    private void HideGrid()
    {
        _grid.Visible = false;
        _save.Visible = false;
        _grid.Rows.Clear();
        _lastRows = new List<ParsedRow>();
    }

    private async Task SaveRowsAsync()
    {
        try
        {
            var count = await _api.SaveAsync(_settings.ApiUrl, _settings.Token!, _lastRows);
            AppendBot($"Saved {count} row(s).");
            HideGrid();
        }
        catch (Exception ex)
        {
            AppendBot("Save failed: " + ex.Message);
        }
    }
}
