namespace LogEase.Desktop;

/// <summary>Microsoft Teams–inspired palette for clear, professional WinForms UI.</summary>
public static class UiTheme
{
    // Teams-like
    public static readonly Color Brand = Color.FromArgb(98, 100, 167);       // #6264A7
    public static readonly Color BrandDark = Color.FromArgb(70, 71, 117);    // #464775
    public static readonly Color BrandHover = Color.FromArgb(88, 90, 155);
    public static readonly Color Header = Color.FromArgb(32, 31, 31);        // near Teams chrome
    public static readonly Color Bg = Color.FromArgb(245, 245, 245);         // #F5F5F5
    public static readonly Color Surface = Color.White;
    public static readonly Color Ink = Color.FromArgb(37, 36, 36);           // #252424
    public static readonly Color Muted = Color.FromArgb(97, 96, 96);         // #616060
    public static readonly Color Line = Color.FromArgb(225, 223, 221);       // #E1DFDD
    public static readonly Color GridHeader = Color.FromArgb(237, 235, 233);
    public static readonly Color Selection = Color.FromArgb(226, 223, 246);  // soft brand tint
    public static readonly Color StatusBg = Color.FromArgb(32, 31, 31);
    public static readonly Color StatusFg = Color.FromArgb(242, 242, 242);

    public static readonly Font Ui = new("Segoe UI", 9.5f);
    public static readonly Font UiBold = new("Segoe UI Semibold", 9.5f);
    public static readonly Font Title = new("Segoe UI Semibold", 13.5f);
    public static readonly Font Caption = new("Segoe UI", 8.25f, FontStyle.Bold);
    public static readonly Font Hint = new("Segoe UI", 9f);

    public static Button PrimaryButton(string text)
    {
        var b = new Button
        {
            Text = text,
            AutoSize = true,
            MinimumSize = new Size(0, 34),
            FlatStyle = FlatStyle.Flat,
            BackColor = Brand,
            ForeColor = Color.White,
            Font = UiBold,
            Padding = new Padding(14, 5, 14, 5),
            Cursor = Cursors.Hand,
            Margin = new Padding(0, 0, 8, 0),
            TextAlign = ContentAlignment.MiddleCenter,
        };
        b.FlatAppearance.BorderSize = 0;
        b.FlatAppearance.MouseOverBackColor = BrandHover;
        b.FlatAppearance.MouseDownBackColor = BrandDark;
        b.EnabledChanged += (_, _) =>
        {
            b.BackColor = b.Enabled ? Brand : Color.FromArgb(180, 180, 190);
            b.ForeColor = Color.White;
        };
        return b;
    }

    public static Button SecondaryButton(string text)
    {
        var b = new Button
        {
            Text = text,
            AutoSize = true,
            MinimumSize = new Size(0, 34),
            FlatStyle = FlatStyle.Flat,
            BackColor = Surface,
            ForeColor = Ink,
            Font = Ui,
            Padding = new Padding(12, 5, 12, 5),
            Cursor = Cursors.Hand,
            Margin = new Padding(0, 0, 8, 0),
            TextAlign = ContentAlignment.MiddleCenter,
        };
        b.FlatAppearance.BorderSize = 1;
        b.FlatAppearance.BorderColor = Line;
        b.FlatAppearance.MouseOverBackColor = GridHeader;
        b.EnabledChanged += (_, _) =>
        {
            b.ForeColor = b.Enabled ? Ink : Muted;
            b.BackColor = Surface;
        };
        return b;
    }

    public static Label HintLabel(string text) =>
        new()
        {
            Text = text,
            AutoSize = false,
            Dock = DockStyle.Top,
            Height = 28,
            ForeColor = Muted,
            Font = Hint,
            TextAlign = ContentAlignment.MiddleLeft,
        };

    public static Label FieldLabel(string text) =>
        new()
        {
            Text = text,
            AutoSize = true,
            ForeColor = Muted,
            Font = Caption,
            Margin = new Padding(0, 8, 10, 0),
            Padding = new Padding(0, 2, 0, 0),
        };

    public static void PaintCardBorder(Control card, PaintEventArgs e)
    {
        using var pen = new Pen(Line);
        e.Graphics.DrawRectangle(pen, 0, 0, card.Width - 1, card.Height - 1);
    }

    public static Button AzureButton(string text)
    {
        var b = new Button
        {
            Text = "   " + text,
            AutoSize = false,
            Height = 44,
            MinimumSize = new Size(0, 44),
            Dock = DockStyle.None,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.White,
            ForeColor = Ink,
            Font = UiBold,
            Padding = new Padding(12, 6, 12, 6),
            Cursor = Cursors.Hand,
            TextAlign = ContentAlignment.MiddleCenter,
            ImageAlign = ContentAlignment.MiddleLeft,
        };
        b.FlatAppearance.BorderSize = 1;
        b.FlatAppearance.BorderColor = Line;
        b.FlatAppearance.MouseOverBackColor = GridHeader;
        b.Image = CreateMicrosoftLogo();
        return b;
    }

    private static Bitmap CreateMicrosoftLogo()
    {
        var bmp = new Bitmap(18, 18);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);
        using var r1 = new SolidBrush(Color.FromArgb(242, 80, 34));
        using var r2 = new SolidBrush(Color.FromArgb(127, 186, 0));
        using var r3 = new SolidBrush(Color.FromArgb(0, 164, 239));
        using var r4 = new SolidBrush(Color.FromArgb(255, 185, 0));
        g.FillRectangle(r1, 0, 0, 8, 8);
        g.FillRectangle(r2, 10, 0, 8, 8);
        g.FillRectangle(r3, 0, 10, 8, 8);
        g.FillRectangle(r4, 10, 10, 8, 8);
        return bmp;
    }

    /// <summary>Teams-style app header with mark + title (no text clipping).</summary>
    public static Panel BuildAppHeader(
        string title,
        int height = 88,
        Image? icon = null,
        EventHandler? onSettingsClick = null,
        EventHandler? onLogoutClick = null,
        UserProfileInfo? profile = null)
    {
        height = Math.Max(height, 88);
        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = height,
            MinimumSize = new Size(0, height),
            BackColor = BrandDark,
            Padding = new Padding(16, 14, 20, 14),
        };

        Control? profileChip = null;
        if (profile is not null && !string.IsNullOrWhiteSpace(profile.Name))
        {
            profileChip = BuildProfileChip(profile);
            profileChip.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            header.Controls.Add(profileChip);
        }

        Button? logout = null;
        if (onLogoutClick is not null)
        {
            logout = HeaderButton("Logout", width: 88);
            logout.Click += onLogoutClick;
            header.Controls.Add(logout);
        }

        Button? settings = null;
        if (onSettingsClick is not null)
        {
            settings = HeaderButton("⚙", width: 38);
            settings.Font = new Font("Segoe UI Symbol", 11f);
            settings.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            settings.Click += onSettingsClick;
            header.Controls.Add(settings);
        }

        void LayoutHeaderChrome()
        {
            // Keep Logout fully visible — never flush against the window edge.
            var right = 20;
            if (logout is not null)
            {
                logout.Location = new Point(Math.Max(0, header.ClientSize.Width - right - logout.Width), 18);
                right += logout.Width + 10;
            }
            if (settings is not null)
            {
                settings.Location = new Point(Math.Max(0, header.ClientSize.Width - right - settings.Width), 18);
                right += settings.Width + 10;
            }
            if (profileChip is not null)
                profileChip.Location = new Point(Math.Max(12, header.ClientSize.Width - right - profileChip.Width), 16);
        }

        header.Resize += (_, _) => LayoutHeaderChrome();
        LayoutHeaderChrome();

        var rightReserve = (logout?.Width ?? 0) + (settings?.Width ?? 0) + (profileChip?.Width ?? 0) + 48;

        Control textStack;
        if (icon is null)
        {
            textStack = BuildHeaderTextStack(title, rightReserve);
            textStack.Dock = DockStyle.Fill;
            header.Controls.Add(textStack);
            textStack.SendToBack();
            return header;
        }

        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = BrandDark,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 48));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        row.Controls.Add(new PictureBox
        {
            Dock = DockStyle.Fill,
            SizeMode = PictureBoxSizeMode.Zoom,
            Image = icon,
            Margin = new Padding(0, 2, 8, 2),
        }, 0, 0);

        textStack = BuildHeaderTextStack(title, rightReserve);
        textStack.Dock = DockStyle.Fill;
        row.Controls.Add(textStack, 1, 0);
        header.Controls.Add(row);
        row.SendToBack();
        return header;
    }

    private static Button HeaderButton(string text, int width)
    {
        var b = new Button
        {
            Text = text,
            AutoSize = false,
            Size = new Size(width, 36),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(88, 90, 155),
            ForeColor = Color.White,
            Font = UiBold,
            Cursor = Cursors.Hand,
            TextAlign = ContentAlignment.MiddleCenter,
        };
        b.FlatAppearance.BorderSize = 0;
        b.FlatAppearance.MouseOverBackColor = BrandHover;
        b.FlatAppearance.MouseDownBackColor = BrandDark;
        return b;
    }

    private static Panel BuildProfileChip(UserProfileInfo profile)
    {
        var displayText = GetProfileDisplayText(profile);
        var chip = new Panel
        {
            AutoSize = false,
            Size = new Size(235, 44),
            BackColor = Color.FromArgb(88, 90, 155),
            Padding = new Padding(6, 5, 8, 5),
        };

        Image? avatarImage = profile.Photo;
        if (avatarImage is null && !string.IsNullOrWhiteSpace(profile.PhotoUrl))
        {
            try
            {
                var bytes = LoadPhotoBytes(profile.PhotoUrl);
                if (bytes is { Length: > 0 })
                {
                    using var ms = new MemoryStream(bytes);
                    // Clone so the stream can be disposed safely.
                    using var tmp = Image.FromStream(ms);
                    avatarImage = new Bitmap(tmp);
                }
            }
            catch
            {
                avatarImage = null;
            }
        }

        var avatar = new PictureBox
        {
            Size = new Size(30, 30),
            Location = new Point(6, 7),
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.FromArgb(120, 122, 190),
        };
        if (avatarImage is not null)
            avatar.Image = avatarImage;
        else
        {
            avatar.Paint += (_, e) =>
            {
                var initials = GetInitials(displayText);
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using var brush = new SolidBrush(Color.FromArgb(198, 199, 230));
                using var font = new Font("Segoe UI Semibold", 10f);
                var size = e.Graphics.MeasureString(initials, font);
                e.Graphics.DrawString(
                    initials,
                    font,
                    brush,
                    (avatar.Width - size.Width) / 2f,
                    (avatar.Height - size.Height) / 2f);
            };
        }

        var text = new Label
        {
            Text = displayText,
            AutoSize = false,
            Location = new Point(44, 0),
            Size = new Size(182, 44),
            ForeColor = Color.White,
            Font = UiBold,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            UseCompatibleTextRendering = true,
        };

        chip.Controls.Add(avatar);
        chip.Controls.Add(text);
        return chip;
    }

    private static string GetProfileDisplayText(UserProfileInfo profile)
    {
        var name = profile.Name.Trim();
        var email = profile.Email?.Trim();
        if (string.IsNullOrWhiteSpace(name)) return email ?? "";
        if (!string.IsNullOrWhiteSpace(email) &&
            name.Equals(email, StringComparison.OrdinalIgnoreCase))
            return email;
        return name;
    }

    private static void LayoutProfileChip(Panel header, Control profileChip, int rightOffset)
    {
        profileChip.Location = new Point(
            Math.Max(12, header.ClientSize.Width - rightOffset - profileChip.Width),
            16);
    }

    private static string GetInitials(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return "?";
        if (parts.Length == 1) return parts[0][..Math.Min(2, parts[0].Length)].ToUpperInvariant();
        return $"{parts[0][0]}{parts[^1][0]}".ToUpperInvariant();
    }

    private static Panel BuildHeaderTextStack(string title, int rightReserve = 0)
    {
        var stack = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = BrandDark,
            Margin = Padding.Empty,
            Padding = new Padding(0, 0, rightReserve, 0),
        };
        stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        stack.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
        stack.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));

        stack.Controls.Add(new Label
        {
            Text = "LOGEASE",
            ForeColor = Color.FromArgb(198, 199, 230),
            Font = Caption,
            AutoSize = false,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.BottomLeft,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            UseCompatibleTextRendering = true,
        }, 0, 0);

        stack.Controls.Add(new Label
        {
            Text = title,
            ForeColor = Color.White,
            Font = Title,
            AutoSize = false,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.TopLeft,
            Margin = Padding.Empty,
            Padding = new Padding(0, 2, 0, 4),
            UseCompatibleTextRendering = true,
        }, 0, 1);

        return stack;
    }

    private static byte[]? LoadPhotoBytes(string photoUrl)
    {
        if (photoUrl.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            var comma = photoUrl.IndexOf(',');
            if (comma < 0 || comma >= photoUrl.Length - 1)
                return null;
            return Convert.FromBase64String(photoUrl[(comma + 1)..]);
        }

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        return client.GetByteArrayAsync(photoUrl).GetAwaiter().GetResult();
    }
}
