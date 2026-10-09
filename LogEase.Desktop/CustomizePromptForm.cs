namespace LogEase.Desktop;

/// <summary>
/// Edit AI interpretation wording: how informal team notes map into LogEase rows.
/// Not a worklog text template — saved per account and used on Generate.
/// </summary>
public sealed class CustomizePromptForm : Form
{
    private readonly AppSettings _settings;
    private readonly LogEaseApiClient _api;
    private readonly RichTextBox _editor;
    private readonly Label _status;

    public CustomizePromptForm(AppSettings settings, LogEaseApiClient api)
    {
        _settings = settings;
        _api = api;

        Text = "Customize AI wording";
        Width = 900;
        Height = 640;
        MinimumSize = new Size(640, 420);
        StartPosition = FormStartPosition.CenterParent;
        Font = UiTheme.Ui;
        BackColor = UiTheme.Bg;
        ForeColor = UiTheme.Ink;
        Icon = AppIcon.GetIcon();
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;

        var header = UiTheme.BuildAppHeader("Customize AI wording", 72);

        var body = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(20, 12, 20, 12),
            BackColor = UiTheme.Bg,
        };

        var tip = new Label
        {
            Dock = DockStyle.Top,
            Height = 56,
            Text = "Tell the AI how your team writes free text (typos, shorthand, project nicknames).\n"
                   + "This is NOT a worklog template — it teaches LogEase how to turn any format into date / project / hours / description.",
            ForeColor = UiTheme.Muted,
            Font = UiTheme.Hint,
            TextAlign = ContentAlignment.TopLeft,
        };

        _editor = new RichTextBox
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = UiTheme.Surface,
            ForeColor = UiTheme.Ink,
            Font = UiTheme.Ui,
            DetectUrls = false,
            WordWrap = true,
            ScrollBars = RichTextBoxScrollBars.Both,
            AcceptsTab = true,
        };

        var editorHost = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(0, 8, 0, 8),
            BackColor = UiTheme.Bg,
        };
        editorHost.Controls.Add(_editor);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 48,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(0, 4, 0, 0),
            BackColor = UiTheme.Bg,
        };
        var reset = UiTheme.SecondaryButton("Reset to default");
        var cancel = UiTheme.SecondaryButton("Cancel");
        cancel.DialogResult = DialogResult.Cancel;
        var save = UiTheme.PrimaryButton("Save for my account");
        reset.Click += async (_, _) =>
        {
            try
            {
                _editor.Text = await _api.GetDefaultAiParseInstructionsAsync(_settings.ApiUrl, _settings.Token)
                               ?? FallbackDefaultWording;
                _status.Text = "Loaded default wording — click Save to store it, or edit first.";
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "LogEase", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        };
        save.Click += async (_, _) => await SaveAsync();
        actions.Controls.Add(save);
        actions.Controls.Add(reset);
        actions.Controls.Add(cancel);

        _status = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 28,
            ForeColor = UiTheme.Muted,
            Font = UiTheme.Hint,
            Text = "Loading…",
        };

        body.Controls.Add(editorHost);
        body.Controls.Add(actions);
        body.Controls.Add(_status);
        body.Controls.Add(tip);

        Controls.Add(body);
        Controls.Add(header);
        CancelButton = cancel;

        Shown += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            var token = await EnsureTokenAsync();
            if (string.IsNullOrWhiteSpace(token))
            {
                _status.Text = "Open Settings and sign in first.";
                _editor.Text = FallbackDefaultWording;
                return;
            }

            string? saved = null;
            string? defaults = null;
            try
            {
                var me = await _api.MeAsync(_settings.ApiUrl, token);
                saved = me?.AiParseInstructions;
            }
            catch (Exception ex)
            {
                _status.Text = "Could not load saved wording: " + ex.Message;
            }

            try
            {
                defaults = await _api.GetDefaultAiParseInstructionsAsync(_settings.ApiUrl, token);
            }
            catch
            {
                defaults = FallbackDefaultWording;
            }

            _editor.Text = string.IsNullOrWhiteSpace(saved)
                ? (defaults ?? FallbackDefaultWording)
                : saved!;
            if (string.IsNullOrWhiteSpace(_status.Text) || _status.Text.StartsWith("Loading"))
            {
                _status.ForeColor = UiTheme.Muted;
                _status.Text = string.IsNullOrWhiteSpace(saved)
                    ? "Showing default wording (not customized yet)."
                    : "Showing your saved AI wording.";
            }
        }
        catch (Exception ex)
        {
            _editor.Text = FallbackDefaultWording;
            _status.ForeColor = Color.Firebrick;
            _status.Text = ex.Message;
            MessageBox.Show(this, ex.Message, "LogEase", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private const string FallbackDefaultWording =
        """
        How our team writes free text (interpret flexibly — typos and shorthand are normal):

        - Project names may be informal or abbreviated (e.g. "company announcement", "perception survey"). Match the closest catalog/Kimai project when possible.
        - Phrases like "other project is X", "also worked on Y" mean multiple projects — create rows for each.
        - Turn messy notes into a short professional description without inventing extra tasks.
        - If they omit hours, assume a normal office day (~7.5–9h) split across mentioned projects.
        - If they omit dates, use only the app From/To date range. Never invent other days.

        Goal: map any free-text format into LogEase rows (date, project, activity, hours, description).
        """;

    private async Task SaveAsync()
    {
        try
        {
            var token = await EnsureTokenAsync();
            if (string.IsNullOrWhiteSpace(token))
                throw new InvalidOperationException("Sign in via Settings first.");

            await _api.UpdateAiParseInstructionsAsync(_settings.ApiUrl, token, _editor.Text);
            _status.ForeColor = Color.FromArgb(15, 118, 70);
            _status.Text = "Saved. Generate table will use this wording.";
            MessageBox.Show(this,
                "AI wording saved for your account.\nGenerate table will use it to interpret free-text notes.",
                "LogEase", MessageBoxButtons.OK, MessageBoxIcon.Information);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            _status.ForeColor = Color.Firebrick;
            _status.Text = ex.Message;
            MessageBox.Show(this, ex.Message, "LogEase", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private async Task<string?> EnsureTokenAsync()
    {
        if (!string.IsNullOrWhiteSpace(_settings.Token))
            return _settings.Token;

        if (string.IsNullOrWhiteSpace(_settings.Email) || string.IsNullOrWhiteSpace(_settings.Password))
            return null;

        var login = await _api.LoginAsync(_settings.ApiUrl, _settings.Email, _settings.Password);
        if (!login.Ok) return null;
        _settings.Token = login.Token;
        _settings.Save();
        return login.Token;
    }
}
