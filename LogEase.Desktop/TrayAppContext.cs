using System.Media;

namespace LogEase.Desktop;

public sealed class TrayAppContext : ApplicationContext
{
    private readonly NotifyIcon _tray;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly AppSettings _settings;
    private readonly LogEaseApiClient _api = new();
    private readonly Form _keepAlive;
    private DateOnly? _lastAlertDate;
    private PromptForm? _openPrompt;
    private NotesForm? _openNotes;
    private CopilotBotForm? _openCopilot;
    private int _reminderBusy;
    private bool _exiting;

    public TrayAppContext(AppSettings? settings = null)
    {
        _settings = settings ?? AppSettings.Load();

        // Hidden MainForm keeps the WinForms message loop alive for tray-only apps.
        // Without this, some Windows builds exit the process after a few minutes of
        // idle tray operation (no visible window).
        _keepAlive = new Form
        {
            Text = "LogEase",
            ShowInTaskbar = false,
            FormBorderStyle = FormBorderStyle.FixedToolWindow,
            StartPosition = FormStartPosition.Manual,
            Location = new Point(-32000, -32000),
            Size = new Size(1, 1),
            Opacity = 0,
            ShowIcon = false,
        };
        _keepAlive.FormClosing += (_, e) =>
        {
            if (_exiting) return;
            e.Cancel = true;
            _keepAlive.Hide();
        };
        _keepAlive.Load += (_, _) => _keepAlive.Hide();
        MainForm = _keepAlive;

        var menu = new ContextMenuStrip();
        menu.Items.Add("Log work…", null, (_, _) => OpenPrompt());
        menu.Items.Add("Work copilot…", null, (_, _) => OpenCopilot());
        menu.Items.Add("Sticky notes…", null, (_, _) => OpenNotes());
        menu.Items.Add("Settings…", null, (_, _) => OpenSettings());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Install / repair shortcuts…", null, (_, _) => RunInstallAgain());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => Exit());

        _tray = new NotifyIcon
        {
            Text = "LogEase Worklog Copilot",
            Icon = AppIcon.GetIcon(),
            Visible = true,
            ContextMenuStrip = menu,
        };
        _tray.DoubleClick += (_, _) => OpenPrompt();
        _tray.BalloonTipClicked += (_, _) => OpenPrompt();

        _timer = new System.Windows.Forms.Timer { Interval = 60_000 };
        _timer.Tick += (_, _) =>
        {
            if (_exiting) return;
            if (Interlocked.CompareExchange(ref _reminderBusy, 1, 0) != 0) return;
            _ = CheckReminderSafeAsync();
        };
        _timer.Start();

        Application.ApplicationExit += (_, _) => TrayLog.Write("ApplicationExit");
        TrayLog.Write("Tray started");

        // Open Log work as soon as the message loop is ready (not only a tray balloon).
        var boot = new System.Windows.Forms.Timer { Interval = 150 };
        boot.Tick += (_, _) =>
        {
            boot.Stop();
            boot.Dispose();
            OpenPrompt();
            // Quiet tray tip only — dialog is already open.
            ShowTrayMessage("LogEase", "Running in the tray. Close the window anytime — the tray icon stays.");
        };
        boot.Start();
    }

    private void RunOnUi(Action action)
    {
        try
        {
            if (_exiting) return;
            if (_keepAlive.IsHandleCreated && _keepAlive.InvokeRequired)
                _keepAlive.BeginInvoke(action);
            else
                action();
        }
        catch (ObjectDisposedException)
        {
            // shutting down
        }
        catch (InvalidOperationException)
        {
            // handle not ready
        }
    }

    private async Task CheckReminderSafeAsync()
    {
        try
        {
            await CheckReminderAsync();
        }
        catch (Exception ex)
        {
            TrayLog.Write("Reminder error: " + ex.Message);
        }
        finally
        {
            Interlocked.Exchange(ref _reminderBusy, 0);
        }
    }

    private async Task ShowStartupStatusAsync()
    {
        try
        {
            var token = await EnsureTokenAsync();
            RunOnUi(() =>
            {
                if (string.IsNullOrWhiteSpace(token))
                {
                    ShowTrayMessage("LogEase is running", "Tray icon is active. Open Settings to connect your account.");
                    return;
                }

                ShowTrayMessage(
                    "LogEase is running",
                    "Tray alert uses your account reminder. Right-click for Log work / Settings.");
            });

            if (string.IsNullOrWhiteSpace(token)) return;

            try
            {
                var me = await _api.MeAsync(_settings.ApiUrl, token);
                var reminder = me?.ReminderTime ?? _settings.ReminderTime;
                RunOnUi(() => ShowTrayMessage(
                    "LogEase is running",
                    me?.ReminderEnabled == false
                        ? "Worklog reminder is off in your account settings."
                        : $"Tray alert follows your account reminder at {reminder}. Right-click for Log work / Settings."));
            }
            catch
            {
                // keep generic startup balloon already shown
            }
        }
        catch (Exception ex)
        {
            TrayLog.Write("Startup status error: " + ex.Message);
            RunOnUi(() => ShowTrayMessage("LogEase is running", "Tray icon is active. Right-click for Log work / Settings."));
        }
    }

    private async Task CheckReminderAsync()
    {
        var token = await EnsureTokenAsync();
        if (string.IsNullOrWhiteSpace(token)) return;

        var (should, msg) = await _api.ReminderStatusAsync(_settings.ApiUrl, token);
        if (!should) return;

        var today = DateOnly.FromDateTime(DateTime.Now);
        if (_lastAlertDate == today) return;

        RunOnUi(() =>
        {
            PlayAlertSound();
            ShowTrayMessage("LogEase reminder", string.IsNullOrWhiteSpace(msg)
                ? "It's time to log today's work."
                : msg,
                ToolTipIcon.Warning);
            _lastAlertDate = today;
        });
    }

    private static void PlayAlertSound()
    {
        try
        {
            SystemSounds.Exclamation.Play();
        }
        catch
        {
            try { Console.Beep(880, 250); } catch { /* ignore */ }
        }
    }

    private void ShowTrayMessage(string title, string text, ToolTipIcon icon = ToolTipIcon.Info)
    {
        try
        {
            if (_tray.Visible)
                _tray.ShowBalloonTip(8000, title, text, icon);
        }
        catch
        {
            // Balloon tip can fail if shell is busy; ignore.
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

    private void RunInstallAgain()
    {
        using var wizard = new InstallForm();
        if (wizard.ShowDialog() != DialogResult.OK || !wizard.InstallAccepted) return;
        try
        {
            var dest = InstallHelper.Install(
                wizard.CreateDesktopShortcut,
                wizard.CreateStartMenuShortcut,
                wizard.StartWithWindows);
            _settings.InstallCompleted = true;
            _settings.InstalledPath = dest;
            _settings.Save();
            MessageBox.Show(
                "Shortcuts updated.\n\n" + dest,
                "LogEase",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "LogEase", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void OpenPrompt()
    {
        try
        {
            if (!EnsureLoggedIn())
                return;

            if (_openPrompt is { IsDisposed: false })
            {
                _openPrompt.Activate();
                return;
            }
            _openPrompt = new PromptForm(_settings, _api);
            // Never assign PromptForm as MainForm — closing Log work must not exit the tray.
            _openPrompt.FormClosed += (_, _) => _openPrompt = null;
            _openPrompt.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "LogEase", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void OpenNotes()
    {
        try
        {
            if (!EnsureLoggedIn())
                return;

            if (_openNotes is { IsDisposed: false })
            {
                _openNotes.Activate();
                return;
            }
            _openNotes = new NotesForm(_settings, _api);
            _openNotes.FormClosed += (_, _) => _openNotes = null;
            _openNotes.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "LogEase", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void OpenCopilot()
    {
        try
        {
            if (!EnsureLoggedIn())
                return;

            if (_openCopilot is { IsDisposed: false })
            {
                _openCopilot.Activate();
                return;
            }
            _openCopilot = new CopilotBotForm(_settings, _api);
            _openCopilot.FormClosed += (_, _) => _openCopilot = null;
            _openCopilot.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "LogEase", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private bool EnsureLoggedIn()
    {
        if (!string.IsNullOrWhiteSpace(_settings.Token))
            return true;

        using var login = new LoginForm(_settings);
        if (login.ShowDialog() != DialogResult.OK || login.Result is not { Success: true })
            return false;

        if (!login.Result.HasKimaiToken)
        {
            using var kimai = new KimaiTokenForm(_settings);
            kimai.ShowDialog();
        }

        return !string.IsNullOrWhiteSpace(_settings.Token);
    }

    private void OpenSettings()
    {
        try
        {
            using var f = new SettingsForm(_settings);
            f.ShowDialog();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "LogEase", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void Exit()
    {
        TrayLog.Write("Exit requested by user");
        _exiting = true;
        _timer.Stop();
        try
        {
            _tray.Visible = false;
            _tray.Dispose();
        }
        catch { /* ignore */ }

        try
        {
            if (_openPrompt is { IsDisposed: false })
                _openPrompt.Close();
        }
        catch { /* ignore */ }

        try
        {
            MainForm = null;
            _keepAlive.Close();
        }
        catch { /* ignore */ }

        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
            _tray.Dispose();
            _keepAlive.Dispose();
        }
        base.Dispose(disposing);
    }
}

internal static class TrayLog
{
    private static readonly string PathFile = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LogEase",
        "tray.log");

    public static void Write(string message)
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(PathFile)!);
            File.AppendAllText(PathFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {message}{Environment.NewLine}");
        }
        catch
        {
            // never throw from logging
        }
    }
}
