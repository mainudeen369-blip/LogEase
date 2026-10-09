namespace LogEase.Desktop;

public sealed class InstallForm : Form
{
    private readonly CheckBox _startWithWindows;
    private readonly CheckBox _desktopShortcut;
    private readonly CheckBox _startMenuShortcut;

    public bool InstallAccepted { get; private set; }
    public bool StartWithWindows => _startWithWindows.Checked;
    public bool CreateDesktopShortcut => _desktopShortcut.Checked;
    public bool CreateStartMenuShortcut => _startMenuShortcut.Checked;

    public InstallForm()
    {
        Text = "Install LogEase";
        Width = 540;
        Height = 480;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        Font = UiTheme.Ui;
        BackColor = UiTheme.Bg;
        ForeColor = UiTheme.Ink;
        Icon = AppIcon.GetIcon();

        var header = UiTheme.BuildAppHeader("Would you like to install?", 88, AppIcon.GetBitmap(44));

        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(24, 16, 24, 16), BackColor = UiTheme.Bg };

        var intro = new Label
        {
            Dock = DockStyle.Top,
            Height = 40,
            ForeColor = UiTheme.Muted,
            Font = UiTheme.Hint,
            Text = "Install LogEase so it lives in your Start Menu and system tray. You can change options anytime.",
        };

        var steps = new Label
        {
            Dock = DockStyle.Top,
            Height = 108,
            ForeColor = UiTheme.Ink,
            Font = UiTheme.Ui,
            Text =
                "What install does:\n" +
                "  1. Copies LogEase to your user AppData folder\n" +
                "  2. Creates Desktop / Start Menu shortcuts (optional)\n" +
                "  3. Optionally starts LogEase when Windows signs in\n" +
                "  4. Then opens the tray app so you can log work",
        };

        var opts = new Panel
        {
            Dock = DockStyle.Top,
            Height = 108,
            BackColor = UiTheme.Surface,
            Padding = new Padding(14),
        };
        opts.Paint += (s, e) => UiTheme.PaintCardBorder((Control)s!, e);

        _desktopShortcut = OptCheck("Create Desktop shortcut", true, 10);
        _startMenuShortcut = OptCheck("Create Start Menu shortcut", true, 38);
        _startWithWindows = OptCheck("Start LogEase when I sign in to Windows", true, 66);
        opts.Controls.Add(_desktopShortcut);
        opts.Controls.Add(_startMenuShortcut);
        opts.Controls.Add(_startWithWindows);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 48,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(0, 8, 0, 0),
            BackColor = UiTheme.Bg,
        };

        var install = UiTheme.PrimaryButton("Yes, install");
        install.Click += (_, _) =>
        {
            InstallAccepted = true;
            DialogResult = DialogResult.OK;
            Close();
        };

        var skip = UiTheme.SecondaryButton("Not now — run portable");
        skip.Click += (_, _) =>
        {
            InstallAccepted = false;
            DialogResult = DialogResult.Cancel;
            Close();
        };

        buttons.Controls.Add(install);
        buttons.Controls.Add(skip);

        body.Controls.Add(buttons);
        body.Controls.Add(opts);
        body.Controls.Add(steps);
        body.Controls.Add(intro);

        Controls.Add(body);
        Controls.Add(header);
        AcceptButton = install;
    }

    private static CheckBox OptCheck(string text, bool on, int y) =>
        new()
        {
            Text = text,
            Checked = on,
            AutoSize = true,
            Location = new Point(12, y),
            ForeColor = UiTheme.Ink,
            Font = UiTheme.Ui,
        };
}
