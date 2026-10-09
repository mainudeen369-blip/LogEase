namespace LogEase.Desktop;

/// <summary>
/// Azure device-code UI: code is large and visible, URL opens with one click, Copy is one click.
/// </summary>
public sealed class AzureDeviceCodeForm : Form
{
    private readonly string _url;
    private readonly string _userCode;
    private readonly TextBox _codeBox;

    public AzureDeviceCodeForm(string verificationUrl, string userCode)
    {
        _url = string.IsNullOrWhiteSpace(verificationUrl)
            ? "https://microsoft.com/devicelogin"
            : verificationUrl.Trim();
        _userCode = userCode.Trim();

        Text = "Sign in with Azure — enter this code";
        ClientSize = new Size(520, 420);
        MinimumSize = new Size(500, 400);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        TopMost = true;
        Font = UiTheme.Ui;
        BackColor = UiTheme.Bg;
        ForeColor = UiTheme.Ink;
        Icon = AppIcon.GetIcon();
        ShowInTaskbar = true;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 8,
            Padding = new Padding(20, 16, 20, 16),
            BackColor = UiTheme.Bg,
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));  // title
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));  // step 1
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));  // open button
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));  // link
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));  // step 2
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));  // CODE (big)
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));  // copy row
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));  // tip

        var title = new Label
        {
            Text = "LOGEASE  ·  Azure device sign-in",
            Dock = DockStyle.Fill,
            Font = UiTheme.UiBold,
            ForeColor = UiTheme.BrandDark,
            TextAlign = ContentAlignment.MiddleLeft,
        };

        var step1 = new Label
        {
            Text = "Step 1 — Open the Azure page (browser opens automatically):",
            Dock = DockStyle.Fill,
            Font = UiTheme.Hint,
            ForeColor = UiTheme.Muted,
            TextAlign = ContentAlignment.BottomLeft,
        };

        var openBtn = UiTheme.PrimaryButton("Open Azure sign-in page");
        openBtn.Dock = DockStyle.Fill;
        openBtn.Click += (_, _) => OpenUrl(_url);

        var link = new LinkLabel
        {
            Text = _url,
            Dock = DockStyle.Fill,
            LinkColor = Color.FromArgb(15, 118, 110),
            ActiveLinkColor = UiTheme.Brand,
            Font = UiTheme.Ui,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
        };
        link.Links.Add(0, link.Text.Length, _url);
        link.LinkClicked += (_, e) => OpenUrl(e.Link?.LinkData as string ?? _url);

        var step2 = new Label
        {
            Text = "Step 2 — Copy this code and paste it on that page:",
            Dock = DockStyle.Fill,
            Font = UiTheme.Hint,
            ForeColor = UiTheme.Muted,
            TextAlign = ContentAlignment.BottomLeft,
        };

        _codeBox = new TextBox
        {
            Text = _userCode,
            ReadOnly = true,
            Dock = DockStyle.Fill,
            Font = new Font("Consolas", 22f, FontStyle.Bold),
            TextAlign = HorizontalAlignment.Center,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.White,
            ForeColor = Color.FromArgb(20, 20, 20),
            Margin = new Padding(0, 4, 0, 4),
        };
        _codeBox.GotFocus += (_, _) => _codeBox.SelectAll();
        _codeBox.Click += (_, _) => _codeBox.SelectAll();

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = Padding.Empty,
        };
        var copyBtn = UiTheme.PrimaryButton("Copy code");
        copyBtn.Height = 36;
        copyBtn.Click += (_, _) =>
        {
            try
            {
                Clipboard.SetText(_userCode);
                copyBtn.Text = "Copied ✓";
                _codeBox.SelectAll();
                _codeBox.Focus();
            }
            catch
            {
                copyBtn.Text = "Copy failed";
            }
        };
        var copyOpen = UiTheme.SecondaryButton("Copy + open page");
        copyOpen.Height = 36;
        copyOpen.Click += (_, _) =>
        {
            try { Clipboard.SetText(_userCode); } catch { /* ignore */ }
            OpenUrl(_url);
            copyBtn.Text = "Copied ✓";
        };
        actions.Controls.Add(copyBtn);
        actions.Controls.Add(copyOpen);

        var tip = new Label
        {
            Text = "Keep this window open. After you paste the code and finish Azure login, LogEase continues automatically.",
            Dock = DockStyle.Fill,
            Font = UiTheme.Hint,
            ForeColor = UiTheme.Muted,
            TextAlign = ContentAlignment.TopLeft,
        };

        root.Controls.Add(title, 0, 0);
        root.Controls.Add(step1, 0, 1);
        root.Controls.Add(openBtn, 0, 2);
        root.Controls.Add(link, 0, 3);
        root.Controls.Add(step2, 0, 4);
        root.Controls.Add(_codeBox, 0, 5);
        root.Controls.Add(actions, 0, 6);
        root.Controls.Add(tip, 0, 7);

        Controls.Add(root);

        Shown += (_, _) =>
        {
            Activate();
            BringToFront();
            OpenUrl(_url);
            _codeBox.Focus();
            _codeBox.SelectAll();
        };
    }

    private static void OpenUrl(string url)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "Could not open browser.\n\n" + ex.Message + "\n\nURL:\n" + url,
                "LogEase",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }
}
