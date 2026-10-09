using System.Text.Json;

namespace LogEase.Desktop;

public sealed class AppSettings
{
    public string ApiUrl { get; set; } = "http://localhost:5055/graphql";
    public string Email { get; set; } = "demo@logease.local";
    public string Password { get; set; } = "Demo@123";
    public string? UserName { get; set; }
    public string? ProfilePhotoUrl { get; set; }
    /// <summary>local or azure</summary>
    public string AuthProvider { get; set; } = "local";
    public string ReminderTime { get; set; } = "17:45";
    public bool ReminderEnabled { get; set; } = true;
    public string? Token { get; set; }
    public bool InstallCompleted { get; set; }
    public string? InstalledPath { get; set; }

    private static string Path =>
        System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "LogEase",
            "desktop-settings.json");

    public static AppSettings Load()
    {
        try
        {
            var path = Path;
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
        }
        catch { /* ignore */ }
        return new AppSettings();
    }

    public void Save()
    {
        var dir = System.IO.Path.GetDirectoryName(Path)!;
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }

    public void ClearSession()
    {
        Token = null;
        UserName = null;
        ProfilePhotoUrl = null;
        AuthProvider = "local";
    }
}
