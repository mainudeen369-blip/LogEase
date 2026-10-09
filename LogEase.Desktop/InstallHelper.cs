using System.Diagnostics;
using Microsoft.Win32;

namespace LogEase.Desktop;

public static class InstallHelper
{
    public static string InstallFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LogEase");

    public static string InstalledExePath => Path.Combine(InstallFolder, "LogEase.Desktop.exe");

    public static bool IsRunningFromInstallFolder()
    {
        try
        {
            var current = Environment.ProcessPath ?? Application.ExecutablePath;
            return string.Equals(
                Path.GetFullPath(current),
                Path.GetFullPath(InstalledExePath),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public static string Install(
        bool desktopShortcut,
        bool startMenuShortcut,
        bool startWithWindows)
    {
        Directory.CreateDirectory(InstallFolder);
        var source = Environment.ProcessPath ?? Application.ExecutablePath;
        var dest = InstalledExePath;

        // Copy exe (+ sibling files if any)
        File.Copy(source, dest, overwrite: true);

        // Also copy icon resource is embedded; optional sibling ico for shortcuts
        var icoSource = Path.Combine(AppContext.BaseDirectory, "Assets", "logease.ico");
        var icoDest = Path.Combine(InstallFolder, "logease.ico");
        if (File.Exists(icoSource))
            File.Copy(icoSource, icoDest, overwrite: true);

        if (desktopShortcut)
            CreateShortcut(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "LogEase.lnk"),
                dest,
                icoDest);

        if (startMenuShortcut)
        {
            var programs = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
            var dir = Path.Combine(programs, "LogEase");
            Directory.CreateDirectory(dir);
            CreateShortcut(Path.Combine(dir, "LogEase.lnk"), dest, icoDest);
        }

        SetStartWithWindows(startWithWindows, dest);
        return dest;
    }

    public static void SetStartWithWindows(bool enabled, string? exePath = null)
    {
        exePath ??= InstalledExePath;
        using var key = Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
        if (key is null) return;
        if (enabled)
            key.SetValue("LogEase", $"\"{exePath}\"");
        else
            key.DeleteValue("LogEase", throwOnMissingValue: false);
    }

    private static void CreateShortcut(string shortcutPath, string targetExe, string iconPath)
    {
        // WScript.Shell COM — reliable on Windows for .lnk
        var shellType = Type.GetTypeFromProgID("WScript.Shell")
                        ?? throw new InvalidOperationException("WScript.Shell unavailable.");
        dynamic shell = Activator.CreateInstance(shellType)!;
        var lnk = shell.CreateShortcut(shortcutPath);
        lnk.TargetPath = targetExe;
        lnk.WorkingDirectory = Path.GetDirectoryName(targetExe)!;
        lnk.Description = "LogEase Worklog Copilot";
        if (File.Exists(iconPath))
            lnk.IconLocation = iconPath;
        else
            lnk.IconLocation = targetExe;
        lnk.Save();
    }

    public static void RelaunchInstalledAndExit()
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = InstalledExePath,
            WorkingDirectory = InstallFolder,
            UseShellExecute = true,
            Arguments = "--installed",
        });
        Application.Exit();
    }
}
