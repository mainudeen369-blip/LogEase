namespace LogEase.Desktop;

public sealed class KimaiTokenForm : Form
{
    private readonly AppSettings _settings;
    private readonly LogEaseApiClient _api = new();
    private readonly TextBox _token;
    private readonly Label _status;

    public KimaiTokenForm(AppSettings settings)
    {
        _settings = settings;
        Text = "LogEase — Connect Kimai";
        Width = 520;
        Height = 340;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        Font = UiTheme.Ui;
        BackColor = UiTheme.Bg;
        ForeColor = UiTheme.Ink;
        Icon = AppIcon.GetIcon();

        var header = UiTheme.BuildAppHeader("Connect Kimai (optional)", 72);

        var body = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(24, 16, 24, 16),
            BackColor = UiTheme.Bg,
        };

        var hint = UiTheme.HintLabel(
            "Paste your Kimai API token to sync projects and save worklogs. " +
            "Your office account is already linked — this maps Kimai to the same profile.");

        _token = new TextBox
        {
            Dock = DockStyle.Top,
            UseSystemPasswordChar = true,
            Font = UiTheme.Ui,
            BorderStyle = BorderStyle.FixedSingle,
            Margin = new Padding(0, 8, 0, 0),
        };

        _status = new Label
        {
            Dock = DockStyle.Top,
            Height = 32,
            ForeColor = UiTheme.Muted,
            Font = UiTheme.Hint,
            TextAlign = ContentAlignment.MiddleLeft,
        };

        var connect = UiTheme.PrimaryButton("Connect Kimai");
        connect.Click += async (_, _) => await ConnectAsync();

        var skip = UiTheme.SecondaryButton("Skip for now");
        skip.Click += (_, _) =>
        {
            DialogResult = DialogResult.Cancel;
            Close();
        };

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(0, 12, 0, 0),
        };
        buttons.Controls.Add(connect);
        buttons.Controls.Add(skip);

        body.Controls.Add(buttons);
        body.Controls.Add(_status);
        body.Controls.Add(_token);
        body.Controls.Add(UiTheme.FieldLabel("KIMAI API TOKEN"));
        body.Controls.Add(hint);

        Controls.Add(body);
        Controls.Add(header);
    }

    private async Task ConnectAsync()
    {
        if (string.IsNullOrWhiteSpace(_settings.Token))
        {
            _status.ForeColor = Color.FromArgb(185, 28, 28);
            _status.Text = "Not signed in.";
            return;
        }

        if (string.IsNullOrWhiteSpace(_token.Text))
        {
            DialogResult = DialogResult.Cancel;
            Close();
            return;
        }

        _status.ForeColor = UiTheme.Muted;
        _status.Text = "Validating token…";
        try
        {
            var me = await _api.UpdateKimaiSettingsAsync(_settings.ApiUrl, _settings.Token, _token.Text.Trim());
            if (me?.KimaiUser is null && !string.IsNullOrWhiteSpace(me?.KimaiError))
            {
                _status.ForeColor = Color.FromArgb(185, 28, 28);
                _status.Text = me.KimaiError;
                return;
            }

            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            _status.ForeColor = Color.FromArgb(185, 28, 28);
            _status.Text = ex.Message;
        }
    }
}
