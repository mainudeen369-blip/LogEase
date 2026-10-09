using System.ComponentModel;

namespace LogEase.Desktop;

public sealed class PromptForm : Form
{
    private readonly AppSettings _settings;
    private readonly LogEaseApiClient _api;
    private readonly TextBox _text;
    private readonly Panel _promptCard;
    private readonly DateTimePicker _from;
    private readonly DateTimePicker _to;
    private readonly DataGridView _grid;
    private readonly Label _status;
    private readonly Button _generate;
    private readonly Button _save;
    private readonly Button _export;
    private readonly Button _addRow;
    private readonly Button _removeRow;
    private readonly CheckBox _srcNotes;
    private readonly CheckBox _srcGitlab;
    private readonly CheckBox _srcMail;
    private readonly BindingList<ParsedRow> _rows = new();
    private List<ProjectInfo> _projects = new();
    private bool _resizingPrompt;
    private bool _fittingActions;
    private int _resizeStartY;
    private int _resizeStartHeight;
    private Panel? _actionsHost;
    private FlowLayoutPanel? _actions;
    private NotesForm? _openNotes;

    public PromptForm(AppSettings settings, LogEaseApiClient api, string? seedText = null)
    {
        _settings = settings;
        _api = api;

        Text = "LogEase — Log work";
        Width = 1120;
        Height = 740;
        MinimumSize = new Size(920, 580);
        StartPosition = FormStartPosition.CenterScreen;
        Font = UiTheme.Ui;
        BackColor = UiTheme.Bg;
        ForeColor = UiTheme.Ink;
        Icon = AppIcon.GetIcon();

        var header = BuildHeader();

        var body = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(20, 12, 20, 12),
            BackColor = UiTheme.Bg,
        };

        // --- prompt card: dates + text + source checkboxes (buttons live BELOW the grip) ---
        // tip(24)+dates(40)+label(26)+sources(36)+pad(32)+text(52) ≈ 210
        const int textDefaultHeight = 52; // ~2 lines
        const int sourcesBarHeight = 36;
        const int promptChromeHeight = 24 + 40 + 26 + sourcesBarHeight + 32; // tip + dates + label + sources + padding
        const int promptDefaultHeight = promptChromeHeight + textDefaultHeight;
        const int promptMinHeight = promptChromeHeight + 44;

        _promptCard = new Panel
        {
            Dock = DockStyle.Top,
            Height = promptDefaultHeight,
            MinimumSize = new Size(0, promptMinHeight),
            BackColor = UiTheme.Surface,
            Padding = new Padding(16, 12, 16, 8),
        };
        _promptCard.Paint += (s, e) => UiTheme.PaintCardBorder((Control)s!, e);

        var tip = new Label
        {
            Dock = DockStyle.Top,
            Height = 24,
            Text = "Write freely and/or tick sources below — AI builds the table. FROM / TO apply when text has no dates.",
            ForeColor = UiTheme.Muted,
            Font = UiTheme.Hint,
            TextAlign = ContentAlignment.MiddleLeft,
        };

        // Flow row: label + equal-width pickers — no clipping from tight TableLayout cells.
        var dates = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 40,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = false,
            Padding = new Padding(0, 4, 0, 4),
            BackColor = UiTheme.Surface,
        };

        _from = StyledDatePicker(DateTime.Today.AddDays(-13));
        _to = StyledDatePicker(DateTime.Today);
        var fromLabel = UiTheme.FieldLabel("FROM");
        fromLabel.AutoSize = true;
        fromLabel.Margin = new Padding(0, 8, 8, 0);
        fromLabel.TextAlign = ContentAlignment.MiddleLeft;
        var toLabel = UiTheme.FieldLabel("TO");
        toLabel.AutoSize = true;
        toLabel.Margin = new Padding(24, 8, 8, 0);
        toLabel.TextAlign = ContentAlignment.MiddleLeft;
        _from.Margin = new Padding(0, 4, 0, 0);
        _to.Margin = new Padding(0, 4, 0, 0);
        dates.Controls.Add(fromLabel);
        dates.Controls.Add(_from);
        dates.Controls.Add(toLabel);
        dates.Controls.Add(_to);

        var promptLabel = new Label
        {
            Text = "WHAT DID YOU WORK ON?",
            Dock = DockStyle.Top,
            Height = 26,
            Margin = new Padding(0),
            Padding = new Padding(0, 4, 0, 0),
            ForeColor = UiTheme.Muted,
            Font = UiTheme.Caption,
            TextAlign = ContentAlignment.MiddleLeft,
        };

        // Plain multiline text — no RichText chrome / native sizing grip.
        _text = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            BorderStyle = BorderStyle.None,
            BackColor = Color.White,
            ForeColor = UiTheme.Ink,
            Font = UiTheme.Ui,
            ScrollBars = ScrollBars.Vertical,
            AcceptsTab = true,
            WordWrap = true,
            MinimumSize = new Size(0, 44),
        };
        if (!string.IsNullOrWhiteSpace(seedText)) _text.Text = seedText;

        var textHost = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(1),
            BackColor = UiTheme.Line,
            MinimumSize = new Size(0, 52),
        };
        var textInner = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(8, 6, 4, 6),
            BackColor = Color.White,
        };
        textInner.Controls.Add(_text);
        textHost.Controls.Add(textInner);

        _srcNotes = new CheckBox
        {
            Text = "Sticky notes",
            AutoSize = true,
            Checked = false,
            ForeColor = UiTheme.Ink,
            Font = UiTheme.Ui,
            Margin = new Padding(0, 6, 16, 0),
        };
        _srcGitlab = new CheckBox
        {
            Text = "GitLab",
            AutoSize = true,
            Checked = false,
            ForeColor = UiTheme.Ink,
            Font = UiTheme.Ui,
            Margin = new Padding(0, 6, 16, 0),
        };
        _srcMail = new CheckBox
        {
            Text = "Mail",
            AutoSize = true,
            Checked = false,
            ForeColor = UiTheme.Ink,
            Font = UiTheme.Ui,
            Margin = new Padding(0, 6, 16, 0),
        };
        var sourcesHint = new Label
        {
            Text = "Include when generating (configure GitLab / mail in Settings)",
            AutoSize = true,
            ForeColor = UiTheme.Muted,
            Font = UiTheme.Hint,
            Margin = new Padding(8, 8, 0, 0),
        };
        var sourcesBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = sourcesBarHeight,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(0, 0, 0, 0),
            BackColor = UiTheme.Surface,
        };
        var sourcesLabel = UiTheme.FieldLabel("SOURCES");
        sourcesLabel.AutoSize = true;
        sourcesLabel.Margin = new Padding(0, 8, 12, 0);
        sourcesBar.Controls.Add(sourcesLabel);
        sourcesBar.Controls.Add(_srcNotes);
        sourcesBar.Controls.Add(_srcGitlab);
        sourcesBar.Controls.Add(_srcMail);
        sourcesBar.Controls.Add(sourcesHint);

        // Add Fill first, then Bottom, then Tops (last Top = topmost).
        _promptCard.Controls.Add(textHost);
        _promptCard.Controls.Add(sourcesBar);
        _promptCard.Controls.Add(promptLabel);
        _promptCard.Controls.Add(dates);
        _promptCard.Controls.Add(tip);

        // Host owns the docked height; FlowLayoutPanel fills it. GetPreferredSize alone
        // often reports a one-row height on first layout and clips the wrapped second row.
        _actionsHost = new Panel
        {
            Dock = DockStyle.Top,
            Height = 100,
            MinimumSize = new Size(0, 48),
            Margin = new Padding(0),
            Padding = new Padding(0),
            BackColor = UiTheme.Bg,
        };
        _actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Padding = new Padding(0, 6, 0, 6),
            Margin = new Padding(0),
            BackColor = UiTheme.Bg,
        };
        _generate = UiTheme.PrimaryButton("Generate table");
        _save = UiTheme.PrimaryButton("Save worklogs");
        _save.Enabled = false;
        _export = UiTheme.SecondaryButton("Export to Excel");
        _export.Enabled = false;
        _addRow = UiTheme.SecondaryButton("Add row");
        _addRow.Enabled = false;
        _removeRow = UiTheme.SecondaryButton("Remove row");
        _removeRow.Enabled = false;
        var customize = UiTheme.SecondaryButton("Customize AI wording");
        var stickyNotes = UiTheme.SecondaryButton("Sticky notes");
        var clearAll = UiTheme.SecondaryButton("Clear all");
        var pending = UiTheme.SecondaryButton("Pending days");
        var close = UiTheme.SecondaryButton("Close");
        var settingsBtn = UiTheme.SecondaryButton("Settings");
        // Small bottom margin so wrapped rows don't sit flush / clip under siblings.
        foreach (var b in new[] { _generate, _save, _export, stickyNotes, customize, clearAll, pending, _addRow, _removeRow, settingsBtn, close })
            b.Margin = new Padding(0, 0, 8, 6);
        _generate.Click += async (_, _) => await SafeUiAsync(GenerateAsync);
        _save.Click += async (_, _) => await SafeUiAsync(SaveAsync);
        _export.Click += (_, _) => ExportToExcel();
        _addRow.Click += (_, _) => AddBlankRow();
        _removeRow.Click += (_, _) => RemoveSelectedRows();
        stickyNotes.Click += (_, _) => OpenStickyNotes();
        customize.Click += (_, _) => OpenCustomizePrompt();
        clearAll.Click += (_, _) => ClearAllData();
        pending.Click += async (_, _) => await SafeUiAsync(ShowPendingDaysAsync);
        close.Click += (_, _) => Close();
        settingsBtn.Click += (_, _) => OpenSettings();
        _actions.Controls.Add(_generate);
        _actions.Controls.Add(_save);
        _actions.Controls.Add(_export);
        _actions.Controls.Add(stickyNotes);
        _actions.Controls.Add(customize);
        _actions.Controls.Add(clearAll);
        _actions.Controls.Add(pending);
        _actions.Controls.Add(_addRow);
        _actions.Controls.Add(_removeRow);
        _actions.Controls.Add(settingsBtn);
        _actions.Controls.Add(close);
        _actionsHost.Controls.Add(_actions);

        _actionsHost.Resize += (_, _) =>
        {
            if (!_fittingActions) ScheduleFitActionsHeight();
        };
        Load += (_, _) => ScheduleFitActionsHeight();
        Shown += (_, _) => ScheduleFitActionsHeight();

        // Drag handle under the prompt card — resizes the text area only.
        var grip = new Panel
        {
            Dock = DockStyle.Top,
            Height = 12,
            Cursor = Cursors.SizeNS,
            BackColor = UiTheme.Bg,
        };
        grip.Paint += (_, e) =>
        {
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var barW = 48;
            var barH = 4;
            var x = (grip.Width - barW) / 2;
            var y = (grip.Height - barH) / 2;
            using var brush = new SolidBrush(Color.FromArgb(180, UiTheme.Muted));
            g.FillRectangle(brush, x, y, barW, barH);
        };
        grip.MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Left) return;
            _resizingPrompt = true;
            _resizeStartY = Cursor.Position.Y;
            _resizeStartHeight = _promptCard.Height;
            grip.Capture = true;
            _promptCard.SuspendLayout();
        };
        grip.MouseMove += (_, _) =>
        {
            if (!_resizingPrompt) return;
            var delta = Cursor.Position.Y - _resizeStartY;
            var max = Math.Max(promptMinHeight + 80, body.ClientSize.Height - 220);
            var next = Math.Clamp(_resizeStartHeight + delta, promptMinHeight, max);
            if (next == _promptCard.Height) return;
            _promptCard.Height = next;
        };
        grip.MouseUp += (_, _) =>
        {
            _resizingPrompt = false;
            grip.Capture = false;
            _promptCard.ResumeLayout(true);
            _promptCard.Invalidate();
        };

        // --- grid section ---
        var gridHeader = new Label
        {
            Dock = DockStyle.Top,
            Height = 32,
            Text = "Preview — click a cell to edit. Use project / activity dropdowns when available.",
            ForeColor = UiTheme.Muted,
            Font = UiTheme.Hint,
            TextAlign = ContentAlignment.BottomLeft,
            Padding = new Padding(0, 0, 0, 6),
        };

        _grid = BuildGrid();
        var gridCard = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(1),
            BackColor = UiTheme.Surface,
            Margin = new Padding(0, 8, 0, 0),
        };
        gridCard.Paint += (s, e) => UiTheme.PaintCardBorder((Control)s!, e);
        gridCard.Controls.Add(_grid);

        var gridSection = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(0, 4, 0, 0),
            BackColor = UiTheme.Bg,
        };
        gridSection.Controls.Add(gridCard);
        gridSection.Controls.Add(gridHeader);

        _status = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 40,
            Padding = new Padding(16, 10, 16, 10),
            BackColor = UiTheme.StatusBg,
            ForeColor = UiTheme.StatusFg,
            Font = UiTheme.Ui,
            Text = "Ready — prompt shows ~2 lines. Drag the grey bar under it to enlarge.",
            TextAlign = ContentAlignment.MiddleLeft,
            UseCompatibleTextRendering = true,
        };

        // Top → bottom: prompt, grip, actions, grid.
        body.Controls.Add(gridSection);
        body.Controls.Add(_actionsHost);
        body.Controls.Add(grip);
        body.Controls.Add(_promptCard);

        Controls.Add(body);
        Controls.Add(_status);
        Controls.Add(header);

        Shown += async (_, _) =>
        {
            // Re-assert height after layout so the text box never collapses to 0.
            _promptCard.Height = promptDefaultHeight;
            ScheduleFitActionsHeight();
            try
            {
                await EnsureTokenAsync();
                await LoadProjectsAsync();
                var me = await _api.MeAsync(_settings.ApiUrl, _settings.Token!);
                if (me is not null)
                {
                    if (!string.IsNullOrWhiteSpace(me.Name)) _settings.UserName = me.Name;
                    if (!string.IsNullOrWhiteSpace(me.Email)) _settings.Email = me.Email;
                    if (!string.IsNullOrWhiteSpace(me.ProfilePhotoUrl)) _settings.ProfilePhotoUrl = me.ProfilePhotoUrl;
                    _settings.Save();
                }
                if (me?.KimaiUser is { } ku)
                    _status.Text = $"Kimai: {ku.DisplayName}"
                                   + (string.IsNullOrWhiteSpace(ku.Email) ? "" : $" · {ku.Email}")
                                   + " — ready.";
                else if (me?.HasKimaiToken == true && !string.IsNullOrWhiteSpace(me.KimaiError))
                    _status.Text = "Kimai token saved, but profile error: " + me.KimaiError;
            }
            catch { /* settings may be needed */ }
            ScheduleFitActionsHeight();
        };
    }

    private void ScheduleFitActionsHeight()
    {
        if (IsDisposed) return;
        if (!IsHandleCreated)
        {
            FitActionsHeight();
            return;
        }

        try
        {
            BeginInvoke(FitActionsHeight);
        }
        catch (InvalidOperationException)
        {
            FitActionsHeight();
        }
    }

    private void FitActionsHeight()
    {
        if (_fittingActions || IsDisposed) return;
        var host = _actionsHost;
        var actions = _actions;
        if (host is null || actions is null) return;
        if (!actions.IsHandleCreated || actions.Width <= 0) return;

        _fittingActions = true;
        try
        {
            // Temporarily give the host room so wrapped rows can lay out, then measure bottoms.
            const int probe = 200;
            if (host.Height < probe)
                host.Height = probe;
            actions.PerformLayout();

            var bottom = actions.Padding.Top;
            foreach (Control c in actions.Controls)
            {
                if (!c.Visible) continue;
                bottom = Math.Max(bottom, c.Bottom + c.Margin.Bottom);
            }

            var needed = Math.Max(bottom + actions.Padding.Bottom, 48);
            if (host.Height != needed)
                host.Height = needed;
        }
        finally
        {
            _fittingActions = false;
        }
    }

    private async Task SafeUiAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            if (IsDisposed) return;
            _status.Text = "Error: " + ex.Message;
            MessageBox.Show(this, ex.Message, "LogEase", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void OpenCustomizePrompt()
    {
        using var dlg = new CustomizePromptForm(_settings, _api);
        dlg.ShowDialog(this);
    }

    private void ClearAllData()
    {
        var confirm = MessageBox.Show(
            this,
            "Clear the prompt text and the preview table?\n\nThis does not delete worklogs already saved on the server/Kimai.",
            "Clear all",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button2);
        if (confirm != DialogResult.Yes) return;

        _text.Clear();
        _rows.Clear();
        _from.Value = DateTime.Today.AddDays(-13);
        _to.Value = DateTime.Today;
        _save.Enabled = false;
        _export.Enabled = false;
        _addRow.Enabled = false;
        _removeRow.Enabled = false;
        _status.Text = "Cleared — ready for a new prompt.";
        _status.ForeColor = UiTheme.StatusFg;
    }

    private void ExportToExcel()
    {
        if (_rows.Count == 0)
        {
            MessageBox.Show(this, "Generate a table first, then export.", "LogEase",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dlg = new SaveFileDialog
        {
            Title = "Export worklogs to Excel",
            Filter = "Excel (*.xls)|*.xls|CSV (*.csv)|*.csv",
            FileName = $"logease-worklogs-{DateTime.Today:yyyy-MM-dd}.xls",
            AddExtension = true,
            OverwritePrompt = true,
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            if (dlg.FilterIndex == 2 || dlg.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            {
                var lines = new List<string>
                {
                    "Date,Project,Activity,Hours,Description,Trust",
                };
                foreach (var r in _rows)
                {
                    static string Esc(string? s) =>
                        "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";
                    lines.Add(string.Join(",",
                        Esc(r.WorkDate), Esc(r.ProjectName), Esc(r.ActivityName),
                        r.Hours.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        Esc(r.Description), Esc(r.Confidence)));
                }
                File.WriteAllLines(dlg.FileName, lines, new System.Text.UTF8Encoding(true));
            }
            else
            {
                ExcelExport.SaveWorklogs(dlg.FileName, _rows);
            }

            _status.Text = "Exported: " + dlg.FileName;
            MessageBox.Show(this, "Exported successfully.\n\n" + dlg.FileName, "LogEase",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "LogEase", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private Panel BuildHeader()
    {
        var profile = new UserProfileInfo
        {
            Name = string.IsNullOrWhiteSpace(_settings.UserName) ? _settings.Email : _settings.UserName!,
            Email = _settings.Email,
            PhotoUrl = _settings.ProfilePhotoUrl,
        };
        return UiTheme.BuildAppHeader(
            "Worklog Copilot",
            height: 88,
            onSettingsClick: (_, _) => OpenSettings(),
            onLogoutClick: (_, _) => Logout(),
            profile: profile);
    }

    private void Logout()
    {
        var confirm = MessageBox.Show(
            this,
            "Sign out from LogEase on this Windows app?",
            "Logout",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button2);
        if (confirm != DialogResult.Yes) return;

        _settings.ClearSession();
        _settings.Save();
        Close();
    }

    private void OpenSettings()
    {
        using var form = new SettingsForm(_settings);
        form.ShowDialog(this);
    }

    private void OpenStickyNotes()
    {
        try
        {
            if (_openNotes is { IsDisposed: false })
            {
                _openNotes.Activate();
                return;
            }

            _openNotes = new NotesForm(_settings, _api);
            _openNotes.FormClosed += (_, _) => _openNotes = null;
            _openNotes.Show(this);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "LogEase", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private async Task ShowPendingDaysAsync()
    {
        try
        {
            await EnsureTokenAsync();
            var token = _settings.Token!;

            using var dlg = new Form
            {
                Text = "Pending worklog days",
                Width = 520,
                Height = 520,
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.Sizable,
                MinimizeBox = false,
                Font = UiTheme.Ui,
                BackColor = UiTheme.Bg,
                ForeColor = UiTheme.Ink,
                Icon = AppIcon.GetIcon(),
                MinimumSize = new Size(480, 420),
            };

            var header = UiTheme.BuildAppHeader("Pending worklog days", 72);

            var rangeBar = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 88,
                Padding = new Padding(16, 8, 16, 4),
                BackColor = UiTheme.Bg,
                WrapContents = true,
            };
            var rangeLabel = UiTheme.FieldLabel("RANGE");
            rangeLabel.Margin = new Padding(0, 10, 8, 0);
            var range = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = 220,
                Font = UiTheme.Ui,
                FlatStyle = FlatStyle.Flat,
            };
            range.Items.AddRange(new object[]
            {
                "This month (1st → today)",
                "Last 30 days",
                "Last 60 days",
                "Year to date",
            });
            range.SelectedIndex = 0;
            rangeBar.Controls.Add(rangeLabel);
            rangeBar.Controls.Add(range);

            var sourceLabel = UiTheme.FieldLabel("SOURCE");
            sourceLabel.Margin = new Padding(0, 10, 8, 0);
            var sourceFilter = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = 160,
                Font = UiTheme.Ui,
                FlatStyle = FlatStyle.Flat,
            };
            sourceFilter.Items.AddRange(new object[] { "Kimai", "Local server" });
            sourceFilter.SelectedIndex = 0;
            rangeBar.Controls.Add(sourceLabel);
            rangeBar.Controls.Add(sourceFilter);

            var info = new Label
            {
                Dock = DockStyle.Top,
                Height = 40,
                Padding = new Padding(16, 4, 16, 4),
                ForeColor = UiTheme.Ink,
                Font = UiTheme.Ui,
                Text = "Loading…",
            };

            var list = new ListBox
            {
                Dock = DockStyle.Fill,
                Font = UiTheme.Ui,
                IntegralHeight = false,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = UiTheme.Surface,
            };

            var close = UiTheme.PrimaryButton("Close");
            close.Dock = DockStyle.Bottom;
            close.Height = 40;
            close.Click += (_, _) => dlg.Close();

            async Task RefreshAsync()
            {
                var today = DateOnly.FromDateTime(DateTime.Today);
                DateOnly fromDate = range.SelectedIndex switch
                {
                    1 => today.AddDays(-29),
                    2 => today.AddDays(-59),
                    3 => new DateOnly(today.Year, 1, 1),
                    _ => new DateOnly(today.Year, today.Month, 1),
                };
                var toDate = today;
                var from = fromDate.ToString("yyyy-MM-dd");
                var to = toDate.ToString("yyyy-MM-dd");

                var sourceName = sourceFilter.SelectedIndex == 0 ? "Kimai" : "Local server";
                var sourceKey = sourceFilter.SelectedIndex == 0 ? "Kimai" : "LocalServer";

                info.Text = $"Loading pending days ({sourceName})…";
                list.Items.Clear();

                var worklogs = await _api.GetWorklogsAsync(_settings.ApiUrl, token, from, to, sourceKey);
                var logged = worklogs
                    .Select(w => w.WorkDate)
                    .Where(d => DateOnly.TryParse(d, out _))
                    .Select(DateOnly.Parse)
                    .ToHashSet();

                var pending = new List<string>();
                for (var d = fromDate; d <= toDate; d = d.AddDays(1))
                {
                    if (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) continue;
                    if (!logged.Contains(d))
                        pending.Add($"{d:yyyy-MM-dd}  ·  {d:dddd}");
                }

                list.Items.AddRange(pending.Cast<object>().ToArray());
                info.Text = pending.Count == 0
                    ? $"All weekdays logged in {sourceName} from {from} to {to}."
                    : $"{pending.Count} pending weekday(s) in {sourceName} from {from} to {to}:";
                _status.Text = pending.Count == 0
                    ? $"No pending worklog days in {sourceName} for selected range."
                    : $"{pending.Count} pending weekday(s) in {sourceName}.";
            }

            async void OnFilterChanged(object? _, EventArgs __)
            {
                try { await RefreshAsync(); }
                catch (Exception ex)
                {
                    info.Text = ex.Message;
                }
            }

            range.SelectedIndexChanged += OnFilterChanged;
            sourceFilter.SelectedIndexChanged += OnFilterChanged;

            var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16, 0, 16, 16) };
            body.Controls.Add(list);
            body.Controls.Add(close);
            dlg.Controls.Add(body);
            dlg.Controls.Add(info);
            dlg.Controls.Add(rangeBar);
            dlg.Controls.Add(header);

            dlg.Shown += async (_, _) =>
            {
                try { await RefreshAsync(); }
                catch (Exception ex)
                {
                    info.Text = ex.Message;
                    MessageBox.Show(dlg, ex.Message, "LogEase", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            };

            dlg.ShowDialog(this);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "LogEase", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private DataGridView BuildGrid()
    {
        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            BackgroundColor = UiTheme.Surface,
            BorderStyle = BorderStyle.None,
            GridColor = UiTheme.Line,
            EnableHeadersVisualStyles = false,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = true,
            AllowUserToResizeColumns = true,
            AllowUserToResizeRows = true,
            RowHeadersVisible = true,
            RowHeadersWidth = 28,
            RowHeadersWidthSizeMode = DataGridViewRowHeadersWidthSizeMode.DisableResizing,
            ReadOnly = false,
            EditMode = DataGridViewEditMode.EditOnEnter,
            SelectionMode = DataGridViewSelectionMode.CellSelect,
            MultiSelect = true,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None,
            ShowCellToolTips = true,
            AutoGenerateColumns = false,
            DataSource = _rows,
            ColumnHeadersHeight = 38,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
            RowTemplate = { Height = 40, MinimumHeight = 36 },
            Font = UiTheme.Ui,
            DefaultCellStyle =
            {
                BackColor = UiTheme.Surface,
                ForeColor = UiTheme.Ink,
                SelectionBackColor = UiTheme.Selection,
                SelectionForeColor = UiTheme.Ink,
                WrapMode = DataGridViewTriState.True,
                Padding = new Padding(4, 4, 4, 4),
                Font = UiTheme.Ui,
            },
        };
        grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = UiTheme.GridHeader,
            ForeColor = UiTheme.Muted,
            Font = UiTheme.Caption,
            Alignment = DataGridViewContentAlignment.MiddleLeft,
            Padding = new Padding(4, 0, 4, 0),
            WrapMode = DataGridViewTriState.False,
        };

        // Fill weights sum ~100 so the grid uses full width (no empty gap on the right).
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(ParsedRow.WorkDate),
            HeaderText = "DATE",
            Name = "WorkDate",
            FillWeight = 10,
            MinimumWidth = 88,
            Resizable = DataGridViewTriState.True,
            DefaultCellStyle = { WrapMode = DataGridViewTriState.False },
        });
        grid.Columns.Add(new DataGridViewComboBoxColumn
        {
            DataPropertyName = nameof(ParsedRow.ProjectName),
            HeaderText = "PROJECT",
            Name = "ProjectName",
            FillWeight = 18,
            MinimumWidth = 130,
            Resizable = DataGridViewTriState.True,
            FlatStyle = FlatStyle.Flat,
            DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton,
        });
        grid.Columns.Add(new DataGridViewComboBoxColumn
        {
            DataPropertyName = nameof(ParsedRow.ActivityName),
            HeaderText = "ACTIVITY",
            Name = "ActivityName",
            FillWeight = 14,
            MinimumWidth = 100,
            Resizable = DataGridViewTriState.True,
            FlatStyle = FlatStyle.Flat,
            DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton,
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(ParsedRow.Hours),
            HeaderText = "HOURS",
            Name = "Hours",
            FillWeight = 7,
            MinimumWidth = 58,
            Resizable = DataGridViewTriState.True,
            ValueType = typeof(decimal),
            DefaultCellStyle =
            {
                WrapMode = DataGridViewTriState.False,
                Format = "0.##",
                Alignment = DataGridViewContentAlignment.MiddleRight,
            },
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(ParsedRow.Description),
            HeaderText = "DESCRIPTION",
            Name = "Description",
            FillWeight = 44,
            MinimumWidth = 220,
            Resizable = DataGridViewTriState.True,
            DefaultCellStyle = { WrapMode = DataGridViewTriState.True },
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(ParsedRow.Confidence),
            HeaderText = "TRUST",
            Name = "Confidence",
            FillWeight = 7,
            MinimumWidth = 58,
            Resizable = DataGridViewTriState.True,
            ReadOnly = true,
            DefaultCellStyle = { WrapMode = DataGridViewTriState.False },
        });

        // Never trap the user in a cell: coerce bad Hours parses and cancel failed commits.
        grid.CellParsing += Grid_CellParsing;
        grid.DataError += (_, e) =>
        {
            e.ThrowException = false;
            if (e.Context.HasFlag(DataGridViewDataErrorContexts.Commit) ||
                e.Context.HasFlag(DataGridViewDataErrorContexts.Parsing))
            {
                try { grid.CancelEdit(); } catch { /* ignore */ }
            }
        };
        grid.CellEndEdit += (_, e) =>
        {
            OnCellEdited(e.RowIndex, e.ColumnIndex);
            var rowIndex = e.RowIndex;
            BeginInvoke(() => AdjustRowHeight(rowIndex));
        };
        grid.ColumnWidthChanged += (_, _) => AdjustAllRowHeights();
        grid.RowsAdded += (_, e) =>
        {
            for (var i = 0; i < e.RowCount; i++)
                AdjustRowHeight(e.RowIndex + i);
        };
        grid.DataBindingComplete += (_, _) => AdjustAllRowHeights();
        grid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (grid.IsCurrentCellDirty && grid.CurrentCell is DataGridViewComboBoxCell)
                grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        grid.EditingControlShowing += Grid_EditingControlShowing;
        return grid;
    }

    private void AdjustAllRowHeights()
    {
        for (var i = 0; i < _grid.Rows.Count; i++)
            AdjustRowHeight(i);
    }

    private void AdjustRowHeight(int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= _grid.Rows.Count) return;
        try
        {
            _grid.AutoResizeRow(rowIndex, DataGridViewAutoSizeRowMode.AllCellsExceptHeader);
            var row = _grid.Rows[rowIndex];
            if (row.Height < 40) row.Height = 40;
            if (row.Height > 160) row.Height = 160;
        }
        catch
        {
            // ignore measurement errors during binding
        }
    }

    private void Grid_CellParsing(object? sender, DataGridViewCellParsingEventArgs e)
    {
        if (e.ColumnIndex < 0 || e.RowIndex < 0) return;
        if (_grid.Columns[e.ColumnIndex].Name != "Hours") return;

        var text = e.Value?.ToString()?.Trim() ?? "";
        if (string.IsNullOrEmpty(text))
        {
            e.Value = 0m;
            e.ParsingApplied = true;
            return;
        }

        if (TryParseHours(text, out var hours))
        {
            e.Value = hours;
            e.ParsingApplied = true;
            return;
        }

        // Keep previous value so the grid can leave the cell instead of trapping focus.
        if (e.RowIndex < _rows.Count)
            e.Value = _rows[e.RowIndex].Hours;
        else
            e.Value = 0m;
        e.ParsingApplied = true;
    }

    private static bool TryParseHours(string text, out decimal hours)
    {
        if (decimal.TryParse(text, System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.CurrentCulture, out hours))
            return true;
        if (decimal.TryParse(text, System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture, out hours))
            return true;

        // Accept both "1,5" and "1.5" regardless of culture.
        var normalized = text.Replace(',', '.');
        return decimal.TryParse(normalized, System.Globalization.NumberStyles.Number,
            System.Globalization.CultureInfo.InvariantCulture, out hours);
    }

    private void Grid_EditingControlShowing(object? sender, DataGridViewEditingControlShowingEventArgs e)
    {
        e.Control.PreviewKeyDown -= EditingControl_PreviewKeyDown;
        e.Control.PreviewKeyDown += EditingControl_PreviewKeyDown;
        e.Control.KeyDown -= EditingControl_KeyDown;
        e.Control.KeyDown += EditingControl_KeyDown;

        if (e.Control is not ComboBox cb) return;
        cb.DropDownStyle = ComboBoxStyle.DropDown;
        cb.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
        cb.AutoCompleteSource = AutoCompleteSource.ListItems;
        cb.FlatStyle = FlatStyle.Flat;
        cb.Font = UiTheme.Ui;
    }

    private static void EditingControl_PreviewKeyDown(object? sender, PreviewKeyDownEventArgs e)
    {
        if (e.KeyCode is Keys.Tab or Keys.Up or Keys.Down or Keys.Left or Keys.Right)
            e.IsInputKey = true;
    }

    private void EditingControl_KeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is ComboBox { DroppedDown: true })
            return;

        if (sender is TextBox tb)
        {
            // Let Left/Right move the caret inside the text when not at an edge.
            if (e.KeyCode == Keys.Left && (tb.SelectionStart > 0 || tb.SelectionLength > 0))
                return;
            if (e.KeyCode == Keys.Right &&
                tb.SelectionStart + tb.SelectionLength < tb.TextLength)
                return;

            // Description is multiline: keep Up/Down for caret unless at first/last line.
            if (tb.Multiline && e.KeyCode is Keys.Up or Keys.Down)
            {
                var line = tb.GetLineFromCharIndex(tb.SelectionStart);
                var lastLine = tb.GetLineFromCharIndex(Math.Max(0, tb.TextLength - 1));
                if (e.KeyCode == Keys.Up && line > 0) return;
                if (e.KeyCode == Keys.Down && line < lastLine) return;
            }
        }

        if (e.KeyCode is not (Keys.Tab or Keys.Up or Keys.Down or Keys.Left or Keys.Right))
            return;

        e.Handled = true;
        e.SuppressKeyPress = true;
        MoveCurrentCell(e.KeyCode, e.Shift);
    }

    private void MoveCurrentCell(Keys key, bool shift)
    {
        if (_grid.CurrentCell is null) return;

        var row = _grid.CurrentCell.RowIndex;
        var col = _grid.CurrentCell.ColumnIndex;
        var deltaRow = 0;
        var deltaCol = 0;

        switch (key)
        {
            case Keys.Tab:
                deltaCol = shift ? -1 : 1;
                break;
            case Keys.Left:
                deltaCol = -1;
                break;
            case Keys.Right:
                deltaCol = 1;
                break;
            case Keys.Up:
                deltaRow = -1;
                break;
            case Keys.Down:
                deltaRow = 1;
                break;
            default:
                return;
        }

        if (!TryGetNextEditableCell(row, col, deltaRow, deltaCol, out var nextRow, out var nextCol))
            return;

        try
        {
            if (!_grid.EndEdit())
                _grid.CancelEdit();
        }
        catch
        {
            try { _grid.CancelEdit(); } catch { /* ignore */ }
        }

        if (nextRow < 0 || nextRow >= _grid.Rows.Count) return;
        if (nextCol < 0 || nextCol >= _grid.Columns.Count) return;
        if (!_grid.Rows[nextRow].Cells[nextCol].Visible) return;

        _grid.CurrentCell = _grid.Rows[nextRow].Cells[nextCol];
        if (_grid.EditMode == DataGridViewEditMode.EditOnEnter &&
            !_grid.Rows[nextRow].Cells[nextCol].ReadOnly)
            _grid.BeginEdit(true);
    }

    private bool TryGetNextEditableCell(
        int row, int col, int deltaRow, int deltaCol, out int nextRow, out int nextCol)
    {
        nextRow = row;
        nextCol = col;
        var rowCount = _grid.Rows.Count;
        var colCount = _grid.Columns.Count;
        if (rowCount == 0 || colCount == 0) return false;

        // Pure vertical move.
        if (deltaRow != 0 && deltaCol == 0)
        {
            nextRow = row + deltaRow;
            if (nextRow < 0 || nextRow >= rowCount) return false;
            nextCol = col;
            return IsCellNavigable(nextRow, nextCol);
        }

        // Horizontal / Tab: wrap across columns, then rows.
        var direction = deltaCol >= 0 ? 1 : -1;
        var r = row;
        var c = col + direction;
        for (var guard = 0; guard < rowCount * colCount; guard++)
        {
            if (c < 0)
            {
                r--;
                c = colCount - 1;
            }
            else if (c >= colCount)
            {
                r++;
                c = 0;
            }

            if (r < 0 || r >= rowCount) return false;
            if (IsCellNavigable(r, c))
            {
                nextRow = r;
                nextCol = c;
                return true;
            }

            c += direction;
        }

        return false;
    }

    private bool IsCellNavigable(int row, int col)
    {
        if (row < 0 || row >= _grid.Rows.Count) return false;
        if (col < 0 || col >= _grid.Columns.Count) return false;
        var cell = _grid.Rows[row].Cells[col];
        return cell.Visible && !cell.ReadOnly;
    }

    private void OnCellEdited(int rowIndex, int columnIndex)
    {
        if (rowIndex < 0 || rowIndex >= _rows.Count) return;
        var row = _rows[rowIndex];
        var col = _grid.Columns[columnIndex].Name;

        if (col == "ProjectName")
        {
            MatchProject(row);
            RefreshActivityComboItems(rowIndex);
        }
        else if (col == "ActivityName")
        {
            MatchActivity(row);
        }
        else if (col == "Description" && _grid.Rows[rowIndex].Cells["Description"].Value is string s)
        {
            _grid.Rows[rowIndex].Cells["Description"].ToolTipText = s;
            AdjustRowHeight(rowIndex);
        }

        _save.Enabled = _rows.Count > 0;
        _export.Enabled = _rows.Count > 0;
        _status.Text = $"Edited · {_rows.Count} row(s). Save when ready.";
        AdjustRowHeight(rowIndex);
    }

    private void MatchProject(ParsedRow row)
    {
        var p = _projects.FirstOrDefault(x =>
            x.Name.Equals(row.ProjectName, StringComparison.OrdinalIgnoreCase));
        if (p is null)
        {
            p = _projects.FirstOrDefault(x =>
                Normalize(x.Name).Contains(Normalize(row.ProjectName)) ||
                Normalize(row.ProjectName).Contains(Normalize(x.Name)));
        }
        if (p is not null)
        {
            row.ProjectId = p.Id;
            row.ProjectName = p.Name;
            var act = p.Activities.FirstOrDefault(a =>
                          a.Name.Equals(row.ActivityName, StringComparison.OrdinalIgnoreCase))
                      ?? p.Activities.FirstOrDefault();
            if (act is not null)
            {
                row.ActivityId = act.Id;
                row.ActivityName = act.Name;
            }
        }
        else
        {
            row.ProjectId = null;
            row.ActivityId = null;
        }
    }

    private void MatchActivity(ParsedRow row)
    {
        var p = _projects.FirstOrDefault(x => x.Id == row.ProjectId)
                ?? _projects.FirstOrDefault(x => x.Name.Equals(row.ProjectName, StringComparison.OrdinalIgnoreCase));
        if (p is null) return;
        var act = p.Activities.FirstOrDefault(a =>
            a.Name.Equals(row.ActivityName, StringComparison.OrdinalIgnoreCase));
        if (act is not null)
        {
            row.ActivityId = act.Id;
            row.ActivityName = act.Name;
        }
    }

    private void RefreshActivityComboItems(int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= _rows.Count) return;
        var row = _rows[rowIndex];
        var p = _projects.FirstOrDefault(x => x.Id == row.ProjectId)
                ?? _projects.FirstOrDefault(x => x.Name.Equals(row.ProjectName, StringComparison.OrdinalIgnoreCase));
        var names = p?.Activities.Select(a => a.Name).ToArray() ?? Array.Empty<string>();
        if (_grid.Rows[rowIndex].Cells["ActivityName"] is DataGridViewComboBoxCell cell)
        {
            cell.Items.Clear();
            cell.Items.AddRange(names);
        }
    }

    private void RefreshComboColumns()
    {
        if (_grid.Columns["ProjectName"] is DataGridViewComboBoxColumn projCol)
        {
            projCol.Items.Clear();
            foreach (var p in _projects)
                projCol.Items.Add(p.Name);
            foreach (var r in _rows)
            {
                if (!string.IsNullOrWhiteSpace(r.ProjectName) && !projCol.Items.Contains(r.ProjectName))
                    projCol.Items.Add(r.ProjectName);
            }
        }

        var allActs = _projects.SelectMany(p => p.Activities.Select(a => a.Name)).Distinct().ToList();
        foreach (var r in _rows)
        {
            if (!string.IsNullOrWhiteSpace(r.ActivityName) && !allActs.Contains(r.ActivityName))
                allActs.Add(r.ActivityName);
        }
        if (_grid.Columns["ActivityName"] is DataGridViewComboBoxColumn actCol)
        {
            actCol.Items.Clear();
            actCol.Items.AddRange(allActs.Cast<object>().ToArray());
        }
    }

    private void AddBlankRow()
    {
        var p = _projects.FirstOrDefault();
        var act = p?.Activities.FirstOrDefault();
        _rows.Add(new ParsedRow
        {
            WorkDate = DateTime.Today.ToString("yyyy-MM-dd"),
            ProjectName = p?.Name ?? "",
            ActivityName = act?.Name ?? "Development",
            Hours = 1,
            Description = "",
            Confidence = "manual",
            ProjectId = p?.Id,
            ActivityId = act?.Id,
        });
        _save.Enabled = true;
        _export.Enabled = true;
        _removeRow.Enabled = true;
        _status.Text = "Added blank row — fill in and Save.";
    }

    private void RemoveSelectedRows()
    {
        var indexes = _grid.SelectedRows.Cast<DataGridViewRow>()
            .Where(r => r.Index >= 0 && r.Index < _rows.Count)
            .Select(r => r.Index)
            .Distinct()
            .OrderByDescending(i => i)
            .ToList();
        foreach (var i in indexes)
            _rows.RemoveAt(i);
        _save.Enabled = _rows.Count > 0;
        _export.Enabled = _rows.Count > 0;
        _removeRow.Enabled = _rows.Count > 0;
        _status.Text = _rows.Count == 0 ? "No rows." : $"{_rows.Count} row(s) left.";
    }

    private async Task LoadProjectsAsync()
    {
        _projects = await _api.GetProjectsAsync(_settings.ApiUrl, _settings.Token!);
        RefreshComboColumns();
    }

    private async Task EnsureTokenAsync()
    {
        if (!string.IsNullOrWhiteSpace(_settings.Token)) return;
        var (ok, token, error) = await _api.LoginAsync(_settings.ApiUrl, _settings.Email, _settings.Password);
        if (!ok || string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException(error ?? "Login failed. Open Settings from the tray icon.");
        _settings.Token = token;
        _settings.Save();
    }

    private async Task GenerateAsync()
    {
        try
        {
            _generate.Enabled = false;
            await EnsureTokenAsync();
            await LoadProjectsAsync();

            var useNotes = _srcNotes.Checked;
            var useGitlab = _srcGitlab.Checked;
            var useMail = _srcMail.Checked;
            var useSources = useNotes || useGitlab || useMail;
            var hasText = !string.IsNullOrWhiteSpace(_text.Text);

            if (!useSources && !hasText)
            {
                MessageBox.Show(this,
                    "Type what you worked on, and/or tick Sticky notes / GitLab / Mail under the prompt.",
                    "LogEase",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            var collected = new List<ParsedRow>();
            var statusParts = new List<string>();

            if (useSources)
            {
                _status.Text = "Loading selected sources…";
                var draft = await _api.DraftFromSourcesAsync(
                    _settings.ApiUrl,
                    _settings.Token!,
                    _from.Value.ToString("yyyy-MM-dd"),
                    _to.Value.ToString("yyyy-MM-dd"),
                    useNotes,
                    useGitlab,
                    useMail);
                collected.AddRange(draft.Rows);
                statusParts.Add($"Sources: {(string.IsNullOrWhiteSpace(draft.SourcesUsed) ? "none" : draft.SourcesUsed)} ({draft.Parser})");
                if (!string.IsNullOrWhiteSpace(draft.Warning))
                    statusParts.Add(draft.Warning);
            }

            if (hasText)
            {
                _status.Text = useSources ? "Also parsing prompt text…" : "Calling AI…";
                var parsed = await _api.ParseAsync(
                    _settings.ApiUrl,
                    _settings.Token!,
                    _text.Text,
                    _from.Value.ToString("yyyy-MM-dd"),
                    _to.Value.ToString("yyyy-MM-dd"));
                collected.AddRange(parsed);
                if (useSources)
                    statusParts.Add($"+ prompt text ({parsed.Count} row(s))");
            }

            _rows.Clear();
            foreach (var r in collected)
            {
                MatchProject(r);
                _rows.Add(r);
            }

            for (var i = 0; i < _rows.Count; i++)
            {
                RefreshActivityComboItems(i);
                if (_grid.Rows[i].Cells["Description"].Value is string s)
                    _grid.Rows[i].Cells["Description"].ToolTipText = s;
            }

            AdjustAllRowHeights();

            _save.Enabled = _rows.Count > 0;
            _export.Enabled = _rows.Count > 0;
            _addRow.Enabled = true;
            _removeRow.Enabled = _rows.Count > 0;
            _status.Text = _rows.Count == 0
                ? (statusParts.Count > 0
                    ? string.Join(" · ", statusParts) + " — no rows returned."
                    : "No rows returned.")
                : $"Preview: {_rows.Count} row(s). Edit freely, then Save."
                  + (statusParts.Count > 0 ? " · " + string.Join(" · ", statusParts) : "");
        }
        catch (Exception ex)
        {
            _settings.Token = null;
            _settings.Save();
            _status.Text = ex.Message;
            MessageBox.Show(this, ex.Message, "LogEase", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            _generate.Enabled = true;
        }
    }

    private async Task SaveAsync()
    {
        try
        {
            _grid.EndEdit();
            _save.Enabled = false;

            foreach (var row in _rows)
            {
                MatchProject(row);
                MatchActivity(row);
            }

            var missing = _rows.Where(r => r.ProjectId is null || r.ActivityId is null).ToList();
            if (missing.Count > 0)
            {
                MessageBox.Show(this,
                    $"{missing.Count} row(s) have unknown project/activity. Pick from the dropdown or add the project in Admin (web).",
                    "LogEase", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _save.Enabled = true;
                _export.Enabled = true;
                return;
            }

            await EnsureTokenAsync();
            var count = await _api.SaveAsync(_settings.ApiUrl, _settings.Token!, _rows.ToList());
            _status.Text = $"Saved {count} worklog(s).";
            MessageBox.Show(this, $"Saved {count} worklog(s) to LogEase.", "LogEase", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            _status.Text = ex.Message;
            MessageBox.Show(this, ex.Message, "LogEase", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            _save.Enabled = _rows.Count > 0;
            _export.Enabled = _rows.Count > 0;
        }
    }

    private static string Normalize(string s) =>
        new string(s.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    private static DateTimePicker StyledDatePicker(DateTime value) =>
        new()
        {
            Format = DateTimePickerFormat.Short,
            Value = value,
            Width = 150,
            Height = 28,
            Font = UiTheme.Ui,
            CalendarForeColor = UiTheme.Ink,
            Margin = new Padding(0, 4, 0, 0),
            Anchor = AnchorStyles.Left | AnchorStyles.Top,
            Dock = DockStyle.None,
        };
}
