namespace LogEase.Desktop;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        Application.SetHighDpiMode(HighDpiMode.SystemAware);

        // Prevent unhandled UI/async exceptions from silently killing the tray app.
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) =>
            ShowCrash("Unexpected error", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
            {
                TrayLog.Write("Unhandled: " + ex);
                // Avoid MessageBox on a terminating thread — it can tear down the process faster.
                if (!e.IsTerminating)
                    ShowCrash("Unexpected error", ex);
            }
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            e.SetObserved();
            try { ShowCrash("Background error", e.Exception); } catch { /* ignore */ }
        };

        var skipInstallPrompt = args.Any(a =>
            a.Equals("--installed", StringComparison.OrdinalIgnoreCase)
            || a.Equals("--skip-install", StringComparison.OrdinalIgnoreCase));

        var settings = AppSettings.Load();

        // First launch (or not yet installed): ask "Would you like to install?"
        if (!skipInstallPrompt
            && !settings.InstallCompleted
            && !InstallHelper.IsRunningFromInstallFolder())
        {
            using var wizard = new InstallForm();
            var result = wizard.ShowDialog();
            if (result == DialogResult.OK && wizard.InstallAccepted)
            {
                try
                {
                    var dest = InstallHelper.Install(
                        wizard.CreateDesktopShortcut,
                        wizard.CreateStartMenuShortcut,
                        wizard.StartWithWindows);
                    settings.InstallCompleted = true;
                    settings.InstalledPath = dest;
                    settings.Save();

                    MessageBox.Show(
                        "LogEase was installed.\n\n" +
                        "• Look for the tray icon near the clock\n" +
                        "• Right-click → Settings to set API URL & login\n" +
                        "• Double-click tray or choose “Log work…” to start\n\n" +
                        $"Installed to:\n{dest}",
                        "LogEase installed",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);

                    // Relaunch from install folder so shortcuts / startup point to one copy
                    if (!InstallHelper.IsRunningFromInstallFolder())
                    {
                        InstallHelper.RelaunchInstalledAndExit();
                        return;
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        "Install failed:\n" + ex.Message + "\n\nContinuing in portable mode.",
                        "LogEase",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }
            }
            else
            {
                // User chose portable — don't ask again unless they reset settings
                settings.InstallCompleted = true;
                settings.Save();
            }
        }

        Application.Run(new TrayAppContext(settings));
    }

    private static void ShowCrash(string title, Exception ex)
    {
        try
        {
            MessageBox.Show(
                ex.Message + "\n\nLogEase will keep running in the tray.",
                "LogEase — " + title,
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
        catch
        {
            // Last resort: never let message-box failure take down the process.
        }
    }
}
