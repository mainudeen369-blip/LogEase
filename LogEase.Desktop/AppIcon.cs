namespace LogEase.Desktop;

public static class AppIcon
{
    private static Icon? _icon;
    private static Bitmap? _bitmap48;

    public static Icon GetIcon()
    {
        if (_icon is not null) return _icon;

        try
        {
            var exe = Environment.ProcessPath ?? Application.ExecutablePath;
            var fromExe = Icon.ExtractAssociatedIcon(exe);
            if (fromExe is not null)
            {
                _icon = fromExe;
                return _icon;
            }
        }
        catch { /* fall through */ }

        try
        {
            var path = FindIcoPath();
            if (path is not null)
            {
                _icon = new Icon(path);
                return _icon;
            }
        }
        catch { /* fall through */ }

        _icon = SystemIcons.Application;
        return _icon;
    }

    public static Bitmap GetBitmap(int size)
    {
        if (size == 48 && _bitmap48 is not null) return _bitmap48;
        try
        {
            var png = Path.Combine(AppContext.BaseDirectory, "Assets", "logease-icon.png");
            if (File.Exists(png))
            {
                using var src = Image.FromFile(png);
                var bmp = new Bitmap(size, size);
                using var g = Graphics.FromImage(bmp);
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.DrawImage(src, 0, 0, size, size);
                if (size == 48) _bitmap48 = bmp;
                return bmp;
            }
        }
        catch { /* fall through */ }

        using var icoBmp = GetIcon().ToBitmap();
        var resized = new Bitmap(size, size);
        using (var g = Graphics.FromImage(resized))
            g.DrawImage(icoBmp, 0, 0, size, size);
        if (size == 48) _bitmap48 = resized;
        return resized;
    }

    private static string? FindIcoPath()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Assets", "logease.ico"),
            Path.Combine(AppContext.BaseDirectory, "logease.ico"),
            Path.Combine(InstallHelper.InstallFolder, "logease.ico"),
        };
        return candidates.FirstOrDefault(File.Exists);
    }
}
