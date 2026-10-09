namespace LogEase.Desktop;

public sealed class SettingsForm : Form
{
    private readonly AppSettings _settings;
    private readonly LogEaseApiClient _apiClient = new();
    private readonly TextBox _apiUrl;
    private readonly TextBox _email;
    private readonly TextBox _password;
    private readonly TextBox _kimaiToken;
    private readonly Label _kimaiStatus;
    private readonly Label _kimaiProfile;
    private readonly DateTimePicker _time;
    private readonly CheckBox _enabled;
    private readonly TextBox _gitlabBaseUrl;
    private readonly TextBox _gitlabUsername;
    private readonly TextBox _gitlabToken;
    private readonly Label _gitlabProfile;
    private readonly Label _gitlabStatus;
    private readonly TextBox _mailHost;
    private readonly TextBox _mailPort;
    private readonly TextBox _mailUser;
    private readonly TextBox _mailPassword;
    private readonly CheckBox _mailSsl;
    private readonly Label _mailStatus;
    private readonly Label _status;
    private readonly List<CollapsibleSection> _sections = new();
    private bool _hasGitlabToken;
    private bool _hasMailSettings;

    public SettingsForm(AppSettings settings)
    {
        _settings = settings;
        Text = "LogEase — Settings";
        Width = 600;
        Height = 640;
        MinimumSize = new Size(560, 520);
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        Font = UiTheme.Ui;
        BackColor = UiTheme.Bg;
        ForeColor = UiTheme.Ink;
        Icon = AppIcon.GetIcon();

        var header = UiTheme.BuildAppHeader("Desktop settings", 78);

        var card = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16, 10, 16, 12),
            BackColor = UiTheme.Bg,
            AutoScroll = true,
        };

        var root = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = UiTheme.Bg,
            Width = 540,
        };

        root.Controls.Add(Hint(
            "Open one section at a time. Save at the bottom applies all changes."));

        _apiUrl = FieldBox(settings.ApiUrl, 480);
        _email = FieldBox(settings.Email, 480);
        _password = FieldBox(settings.Password, 480);
        _password.UseSystemPasswordChar = true;

        var account = Section(
            "Account",
            "API URL and login for the tray app",
            expanded: true,
            Hint("Used when signing in with email/password. Azure sign-in keeps its own session."),
            Labeled("API URL", _apiUrl),
            Labeled("Email", _email),
            Labeled("Password", _password));

        _kimaiToken = FieldBox("", 480);
        _kimaiToken.UseSystemPasswordChar = true;
        _kimaiProfile = StatusLabel("Kimai: not connected yet", UiTheme.Ink);
        _kimaiStatus = StatusLabel("Paste your Kimai API token from Profile → API Access.", UiTheme.Muted);

        var kimai = Section(
            "Kimai",
            "Timesheet API token",
            expanded: false,
            _kimaiProfile,
            _kimaiStatus,
            Labeled("Kimai API token", _kimaiToken));

        _enabled = new CheckBox
        {
            Text = "Enable end-of-day alert",
            Checked = settings.ReminderEnabled,
            AutoSize = true,
            ForeColor = UiTheme.Ink,
            Font = UiTheme.Ui,
            Margin = new Padding(0, 4, 0, 4),
        };
        _time = new DateTimePicker
        {
            Format = DateTimePickerFormat.Custom,
            CustomFormat = "HH:mm",
            ShowUpDown = true,
            Width = 110,
            Font = UiTheme.Ui,
            Margin = new Padding(0, 4, 0, 4),
        };
        if (TimeOnly.TryParse(settings.ReminderTime, out var t))
            _time.Value = DateTime.Today.Add(t.ToTimeSpan());
        else
            _time.Value = DateTime.Today.AddHours(17).AddMinutes(45);

        var reminder = Section(
            "Worklog reminder",
            "Tray end-of-day alert",
            expanded: false,
            Labeled("Alert time", _time),
            _enabled);

        _gitlabBaseUrl = FieldBox("https://gitlab.com", 480);
        _gitlabUsername = FieldBox("", 480);
        _gitlabToken = FieldBox("", 480);
        _gitlabToken.UseSystemPasswordChar = true;
        _gitlabProfile = StatusLabel("GitLab: not connected yet", UiTheme.Ink);
        _gitlabStatus = StatusLabel(
            "Token needs read_api / read_user. Use your company GitLab URL if not gitlab.com.",
            UiTheme.Muted);

        var gitlab = Section(
            "GitLab",
            "Optional — for summarize from GitLab activity",
            expanded: false,
            _gitlabProfile,
            _gitlabStatus,
            Labeled("GitLab base URL", _gitlabBaseUrl),
            Labeled("Username (optional)", _gitlabUsername),
            Labeled("Personal access token", _gitlabToken));

        _mailHost = FieldBox("", 480);
        _mailPort = FieldBox("993", 120);
        _mailUser = FieldBox("", 480);
        _mailPassword = FieldBox("", 480);
        _mailPassword.UseSystemPasswordChar = true;
        _mailSsl = new CheckBox
        {
            Text = "Use SSL",
            Checked = true,
            AutoSize = true,
            ForeColor = UiTheme.Ink,
            Font = UiTheme.Ui,
            Margin = new Padding(0, 4, 0, 4),
        };
        _mailStatus = StatusLabel(
            "Company Outlook often blocks IMAP. Ask IT, or skip and use notes + GitLab.",
            UiTheme.Muted);

        var mail = Section(
            "Mail (IMAP)",
            "Optional — company mail may not support app passwords",
            expanded: false,
            _mailStatus,
            Labeled("IMAP host", _mailHost),
            Labeled("Port", _mailPort),
            Labeled("IMAP user", _mailUser),
            Labeled("IMAP password / app password", _mailPassword),
            _mailSsl);

        root.Controls.Add(account);
        root.Controls.Add(kimai);
        root.Controls.Add(reminder);
        root.Controls.Add(gitlab);
        root.Controls.Add(mail);

        _status = StatusLabel("Changes are saved to your LogEase account.", UiTheme.Muted);
        root.Controls.Add(_status);

        var save = UiTheme.PrimaryButton("Save");
        save.Margin = new Padding(0, 8, 0, 8);
        save.Click += async (_, _) => await SaveAsync();
        root.Controls.Add(save);

        card.Controls.Add(root);
        Controls.Add(card);
        Controls.Add(header);

        Shown += async (_, _) => await LoadAccountSettingsAsync();
    }

    private CollapsibleSection Section(string title, string subtitle, bool expanded, params Control[] content)
    {
        var section = new CollapsibleSection(title, subtitle, expanded, onExpand: OnSectionExpand);
        section.AddContent(content);
        _sections.Add(section);
        return section;
    }

    private void OnSectionExpand(CollapsibleSection opened)
    {
        foreach (var s in _sections)
        {
            if (!ReferenceEquals(s, opened))
                s.SetExpanded(false);
        }
    }

    private static Label Hint(string text) =>
        new()
        {
            Text = text,
            AutoSize = true,
            ForeColor = UiTheme.Muted,
            Font = UiTheme.Hint,
            Margin = new Padding(0, 0, 0, 10),
            MaximumSize = new Size(520, 0),
        };

    private static Label StatusLabel(string text, Color color) =>
        new()
        {
            Text = text,
            AutoSize = true,
            ForeColor = color,
            Font = UiTheme.Hint,
            Margin = new Padding(0, 2, 0, 6),
            MaximumSize = new Size(500, 0),
        };

    private static Control Labeled(string label, Control c)
    {
        var p = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Margin = new Padding(0, 0, 0, 2),
        };
        p.Controls.Add(UiTheme.FieldLabel(label));
        p.Controls.Add(c);
        return p;
    }

    private void ApplyMe(UserSettings me)
    {
        _enabled.Checked = me.ReminderEnabled;
        if (TimeOnly.TryParse(me.ReminderTime, out var time))
            _time.Value = DateTime.Today.Add(time.ToTimeSpan());
        ApplyKimaiUi(me);
        ApplyGitlabUi(me);
        ApplyMailUi(me);
    }

    private void ApplyKimaiUi(UserSettings me)
    {
        if (me.KimaiUser is { } ku)
        {
            _kimaiProfile.ForeColor = Color.FromArgb(15, 118, 70);
            _kimaiProfile.Text = ku.DisplayName
                                 + (string.IsNullOrWhiteSpace(ku.Email) ? "" : $"  ·  {ku.Email}")
                                 + $"  (@{ku.Username})";
            _kimaiStatus.ForeColor = UiTheme.Muted;
            _kimaiStatus.Text = "Connected. Enter a new token only to replace it.";
        }
        else if (me.HasKimaiToken)
        {
            _kimaiProfile.ForeColor = Color.Firebrick;
            _kimaiProfile.Text = "Token saved — profile not loaded";
            _kimaiStatus.ForeColor = Color.Firebrick;
            _kimaiStatus.Text = string.IsNullOrWhiteSpace(me.KimaiError)
                ? "Could not read Kimai user details."
                : me.KimaiError;
        }
        else
        {
            _kimaiProfile.ForeColor = UiTheme.Ink;
            _kimaiProfile.Text = "Kimai: not connected yet";
            _kimaiStatus.ForeColor = UiTheme.Muted;
            _kimaiStatus.Text = "Paste your Kimai API token from Profile → API Access.";
        }
    }

    private void ApplyGitlabUi(UserSettings me)
    {
        _hasGitlabToken = me.HasGitlabToken;
        if (!string.IsNullOrWhiteSpace(me.GitlabConnectedUser))
        {
            _gitlabProfile.ForeColor = Color.FromArgb(15, 118, 70);
            _gitlabProfile.Text = $"GitLab connected as {me.GitlabConnectedUser}";
            var detail = me.GitlabBaseUrl ?? "https://gitlab.com";
            if (!string.IsNullOrWhiteSpace(me.GitlabUsername))
                detail += $" · @{me.GitlabUsername}";
            _gitlabStatus.ForeColor = UiTheme.Muted;
            _gitlabStatus.Text = detail;
        }
        else if (me.HasGitlabToken)
        {
            _gitlabProfile.ForeColor = Color.Firebrick;
            _gitlabProfile.Text = "Token saved — GitLab profile not loaded";
            _gitlabStatus.ForeColor = Color.Firebrick;
            _gitlabStatus.Text = string.IsNullOrWhiteSpace(me.GitlabError)
                ? $"Trying {me.GitlabBaseUrl ?? "https://gitlab.com"}. Fix base URL if needed, then paste token again."
                : me.GitlabError;
        }
        else
        {
            _gitlabProfile.ForeColor = UiTheme.Ink;
            _gitlabProfile.Text = "GitLab: not connected yet";
            _gitlabStatus.ForeColor = UiTheme.Muted;
            _gitlabStatus.Text =
                "Token needs read_api / read_user. Use your company GitLab URL if not gitlab.com.";
        }

        if (!string.IsNullOrWhiteSpace(me.GitlabBaseUrl))
            _gitlabBaseUrl.Text = me.GitlabBaseUrl;
        if (!string.IsNullOrWhiteSpace(me.GitlabUsername))
            _gitlabUsername.Text = me.GitlabUsername;
    }

    private void ApplyMailUi(UserSettings me)
    {
        _hasMailSettings = me.HasMailSettings;
        if (me.HasMailSettings)
        {
            _mailStatus.ForeColor = Color.FromArgb(15, 118, 70);
            _mailStatus.Text = $"IMAP configured for {me.MailImapUser} @ {me.MailImapHost}. Enter a new password only to replace.";
        }
        else
        {
            _mailStatus.ForeColor = UiTheme.Muted;
            _mailStatus.Text =
                "Company Outlook often blocks IMAP. Ask IT, or skip and use notes + GitLab.";
        }

        if (!string.IsNullOrWhiteSpace(me.MailImapHost))
            _mailHost.Text = me.MailImapHost;
        if (me.MailImapPort is > 0)
            _mailPort.Text = me.MailImapPort.Value.ToString();
        if (!string.IsNullOrWhiteSpace(me.MailImapUser))
            _mailUser.Text = me.MailImapUser;
        _mailSsl.Checked = me.MailImapUseSsl;
    }

    private async Task LoadAccountSettingsAsync()
    {
        try
        {
            var token = await EnsureTokenAsync();
            if (string.IsNullOrWhiteSpace(token)) return;

            var me = await _apiClient.MeAsync(_settings.ApiUrl, token);
            if (me is null) return;

            ApplyMe(me);
        }
        catch (Exception ex)
        {
            _status.ForeColor = Color.Firebrick;
            _status.Text = ex.Message;
        }
    }

    private async Task SaveAsync()
    {
        var previousToken = _settings.Token;
        var previousApiUrl = _settings.ApiUrl;

        _settings.ApiUrl = _apiUrl.Text.Trim();
        _settings.Email = _email.Text.Trim();
        _settings.Password = _password.Text;
        _settings.ReminderEnabled = _enabled.Checked;
        _settings.ReminderTime = _time.Value.ToString("HH:mm");
        var apiUrlChanged = !string.Equals(previousApiUrl, _settings.ApiUrl, StringComparison.OrdinalIgnoreCase);
        if (apiUrlChanged)
            _settings.Token = null;
        _settings.Save();

        try
        {
            var token = await EnsureTokenForSaveAsync(previousToken, apiUrlChanged);
            if (string.IsNullOrWhiteSpace(token))
                throw new InvalidOperationException(
                    "Not signed in. Close Settings, sign in from the tray (Azure or email), then save again.");

            await _apiClient.UpdateReminderAsync(_settings.ApiUrl, token, _enabled.Checked, _time.Value.ToString("HH:mm"));

            UserSettings? me = null;
            var savedParts = new List<string>();

            if (!string.IsNullOrWhiteSpace(_kimaiToken.Text))
            {
                me = await _apiClient.UpdateKimaiSettingsAsync(_settings.ApiUrl, token, _kimaiToken.Text.Trim());
                _kimaiToken.Clear();
                savedParts.Add(me?.KimaiUser is { } ku
                    ? $"Kimai connected as {ku.DisplayName}."
                    : "Kimai token saved.");
            }

            // Optional: only push GitLab when the user pastes a new token (or edits username with a new token).
            // Do not re-validate an existing/expired token on every Save — that blocked reminder-only saves.
            var newGitlabToken = _gitlabToken.Text.Trim();
            if (newGitlabToken.Length > 0)
            {
                me = await _apiClient.UpdateGitlabSettingsAsync(
                    _settings.ApiUrl,
                    token,
                    newGitlabToken,
                    string.IsNullOrWhiteSpace(_gitlabBaseUrl.Text) ? null : _gitlabBaseUrl.Text.Trim(),
                    string.IsNullOrWhiteSpace(_gitlabUsername.Text) ? null : _gitlabUsername.Text.Trim());
                _gitlabToken.Clear();
                if (!string.IsNullOrWhiteSpace(me?.GitlabConnectedUser))
                    savedParts.Add($"GitLab connected as {me.GitlabConnectedUser}.");
                else if (!string.IsNullOrWhiteSpace(me?.GitlabError))
                    savedParts.Add("GitLab optional — not connected: token rejected (see GitLab section).");
                else if (me?.HasGitlabToken == true)
                    savedParts.Add("GitLab token saved.");
            }

            if (_mailHost.Text.Trim().Length > 0
                || _mailUser.Text.Trim().Length > 0
                || _mailPassword.Text.Trim().Length > 0)
            {
                var portNum = int.TryParse(_mailPort.Text.Trim(), out var p) ? p : 993;
                me = await _apiClient.UpdateMailSettingsAsync(
                    _settings.ApiUrl,
                    token,
                    string.IsNullOrWhiteSpace(_mailHost.Text) ? null : _mailHost.Text.Trim(),
                    portNum,
                    string.IsNullOrWhiteSpace(_mailUser.Text) ? null : _mailUser.Text.Trim(),
                    string.IsNullOrWhiteSpace(_mailPassword.Text) ? null : _mailPassword.Text,
                    _mailSsl.Checked);
                _mailPassword.Clear();
                if (me?.HasMailSettings == true)
                    savedParts.Add("Mail IMAP settings saved.");
            }

            me ??= await _apiClient.MeAsync(_settings.ApiUrl, token);
            if (me is not null)
                ApplyMe(me);

            _settings.Token = token;
            _settings.Save();

            _status.ForeColor = Color.FromArgb(15, 118, 70);
            _status.Text = savedParts.Count > 0
                ? string.Join(" ", savedParts)
                : "Settings saved.";
            MessageBox.Show(this, _status.Text, "LogEase", MessageBoxButtons.OK, MessageBoxIcon.Information);
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

        var login = await _apiClient.LoginAsync(_settings.ApiUrl, _settings.Email, _settings.Password);
        if (!login.Ok) return null;
        _settings.Token = login.Token;
        _settings.Save();
        return login.Token;
    }

    private async Task<string?> EnsureTokenForSaveAsync(string? previousToken, bool apiUrlChanged)
    {
        if (!string.IsNullOrWhiteSpace(_settings.Token))
            return _settings.Token;

        if (!apiUrlChanged && !string.IsNullOrWhiteSpace(previousToken))
        {
            _settings.Token = previousToken;
            return previousToken;
        }

        return await EnsureTokenAsync();
    }

    private static TextBox FieldBox(string text, int width) =>
        new()
        {
            Width = width,
            Text = text,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = UiTheme.Surface,
            ForeColor = UiTheme.Ink,
            Font = UiTheme.Ui,
            Margin = new Padding(0, 4, 0, 4),
        };
}
