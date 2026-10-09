namespace LogEase.Desktop;

public sealed class LoginForm : Form
{
    private readonly AppSettings _settings;
    private readonly LogEaseApiClient _api = new();
    private readonly TextBox _email;
    private readonly TextBox _password;
    private readonly TextBox _status;
    private readonly Button _azureBtn;
    private readonly Button _copyError;
    private AuthConfig? _authConfig;

    public LoginResponse? Result { get; private set; }

    public LoginForm(AppSettings settings)
    {
        _settings = settings;
        Text = "LogEase — Sign in";
        Width = 560;
        Height = 680;
        MinimumSize = new Size(520, 620);
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        Font = UiTheme.Ui;
        BackColor = UiTheme.Bg;
        ForeColor = UiTheme.Ink;
        Icon = AppIcon.GetIcon();

        var header = UiTheme.BuildAppHeader("Sign in to LogEase", 80);

        var card = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(24, 16, 24, 16),
            BackColor = UiTheme.Bg,
            AutoScroll = true,
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 1,
            BackColor = UiTheme.Bg,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var hint = UiTheme.HintLabel("Use your office Azure AD account or email/password.");
        hint.Dock = DockStyle.Fill;
        hint.Height = 32;
        layout.Controls.Add(hint, 0, layout.RowCount++);

        _azureBtn = UiTheme.AzureButton("Sign in with Azure");
        _azureBtn.Dock = DockStyle.Fill;
        _azureBtn.Height = 44;
        _azureBtn.Margin = new Padding(0, 4, 0, 8);
        _azureBtn.Click += async (_, _) => await SignInWithAzureAsync();
        layout.Controls.Add(_azureBtn, 0, layout.RowCount++);

        var divider = new Label
        {
            Text = "—  or continue with email  —",
            Height = 28,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = UiTheme.Muted,
            Font = UiTheme.Hint,
            Dock = DockStyle.Fill,
        };
        layout.Controls.Add(divider, 0, layout.RowCount++);

        _email = FieldBox(settings.Email);
        layout.Controls.Add(Labeled("EMAIL", _email), 0, layout.RowCount++);

        _password = FieldBox(settings.Password);
        _password.UseSystemPasswordChar = true;
        layout.Controls.Add(Labeled("PASSWORD", _password), 0, layout.RowCount++);

        var signIn = UiTheme.PrimaryButton("Sign in with email");
        signIn.Dock = DockStyle.Fill;
        signIn.Margin = new Padding(0, 8, 0, 4);
        signIn.Click += async (_, _) => await SignInWithEmailAsync();
        layout.Controls.Add(signIn, 0, layout.RowCount++);

        _status = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Height = 140,
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.FromArgb(255, 247, 237),
            ForeColor = Color.FromArgb(153, 27, 27),
            Font = new Font("Consolas", 8.5f),
            Visible = false,
            WordWrap = true,
        };
        layout.Controls.Add(_status, 0, layout.RowCount++);

        _copyError = UiTheme.SecondaryButton("Copy full error");
        _copyError.Visible = false;
        _copyError.Dock = DockStyle.Left;
        _copyError.Click += (_, _) =>
        {
            try
            {
                Clipboard.SetText(_status.Text);
                _copyError.Text = "Copied ✓";
            }
            catch
            {
                _copyError.Text = "Copy failed";
            }
        };
        layout.Controls.Add(_copyError, 0, layout.RowCount++);

        var demo = new Label
        {
            Text = "Demo: demo@logease.local / Demo@123",
            Height = 22,
            ForeColor = UiTheme.Muted,
            Font = UiTheme.Hint,
            Dock = DockStyle.Fill,
        };
        layout.Controls.Add(demo, 0, layout.RowCount++);

        card.Controls.Add(layout);
        Controls.Add(card);
        Controls.Add(header);

        AcceptButton = signIn;
        Shown += async (_, _) => await LoadAuthConfigAsync();
    }

    private async Task LoadAuthConfigAsync()
    {
        _authConfig = await _api.GetAuthConfigAsync(_settings.ApiUrl);
        _azureBtn.Visible = _authConfig?.AzureEnabled == true;
    }

    private void ShowError(string message)
    {
        _status.Visible = true;
        _status.Text = message;
        _copyError.Visible = true;
        _copyError.Text = "Copy full error";
        MessageBox.Show(
            this,
            message,
            "LogEase — Azure error (full details)",
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning);
    }

    private async Task SignInWithAzureAsync()
    {
        if (_authConfig is null)
            await LoadAuthConfigAsync();
        if (_authConfig is null || !_authConfig.AzureEnabled)
        {
            ShowError("Azure sign-in is not configured on the API.");
            return;
        }

        SetBusy(true);
        _status.Visible = false;
        _copyError.Visible = false;
        _status.Text = "";
        try
        {
            var azure = new AzureAuthHelper(_authConfig);
            var (ok, idToken, accessToken, error) = await azure.SignInAsync(this);
            if (!ok || string.IsNullOrWhiteSpace(idToken))
            {
                ShowError(error ?? "Azure sign-in failed.");
                return;
            }

            var login = await _api.LoginWithAzureAsync(_settings.ApiUrl, idToken, accessToken);
            if (!login.Success || string.IsNullOrWhiteSpace(login.Token))
            {
                ShowError(login.Error ?? "Azure sign-in failed when calling LogEase API.");
                return;
            }

            ApplyLogin(login, "azure");
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            // API / GraphQL errors are already clear — don't wrap with Azure Entra hints.
            ShowError(ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task SignInWithEmailAsync()
    {
        SetBusy(true);
        _status.Visible = false;
        _copyError.Visible = false;
        _status.Text = "";
        try
        {
            var login = await _api.LoginWithEmailAsync(_settings.ApiUrl, _email.Text.Trim(), _password.Text);
            if (!login.Success || string.IsNullOrWhiteSpace(login.Token))
            {
                ShowError(login.Error ?? "Invalid email or password.");
                return;
            }

            ApplyLogin(login, "local");
            _settings.Email = _email.Text.Trim();
            _settings.Password = _password.Text;
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            ShowError(ex.ToString());
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void ApplyLogin(LoginResponse login, string provider)
    {
        Result = login;
        _settings.Token = login.Token;
        _settings.Email = login.Email ?? _settings.Email;
        _settings.UserName = login.Name;
        _settings.ProfilePhotoUrl = login.ProfilePhotoUrl;
        _settings.AuthProvider = provider;
        _settings.Save();
    }

    private void SetBusy(bool busy)
    {
        _azureBtn.Enabled = !busy;
        _email.Enabled = !busy;
        _password.Enabled = !busy;
    }

    private static TextBox FieldBox(string text) =>
        new()
        {
            Text = text,
            Dock = DockStyle.Fill,
            Font = UiTheme.Ui,
            BorderStyle = BorderStyle.FixedSingle,
            Margin = new Padding(0, 4, 0, 0),
            Height = 28,
        };

    private static Panel Labeled(string label, Control field)
    {
        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0, 0, 0, 6),
        };
        row.Controls.Add(UiTheme.FieldLabel(label), 0, 0);
        row.Controls.Add(field, 0, 1);
        return row;
    }
}
