using System.ComponentModel;

namespace LogEase.Desktop;

/// <summary>Phase A: sticky notes + summarize to worklog draft (same editable table UX as AI Entry).</summary>
public sealed class NotesForm : Form
{
    private readonly AppSettings _settings;
    private readonly LogEaseApiClient _api;
    private readonly TextBox _noteDate = new();
    private readonly TextBox _body = new() { Multiline = true, ScrollBars = ScrollBars.Vertical };
    private readonly TextBox _from = new();
    private readonly TextBox _to = new();
    private readonly CheckBox _includeNotes = new() { Text = "Sticky notes", Checked = true, AutoSize = true };
    private readonly CheckBox _includeGitlab = new() { Text = "GitLab", AutoSize = true };
    private readonly CheckBox _includeMail = new() { Text = "Mail", AutoSize = true };
    private readonly BindingList<StickyNoteInfo> _notes = new();
    private readonly BindingList<ParsedRow> _draft = new();
    private readonly DataGridView _notesGrid;
    private readonly DataGridView _draftGrid;
    private readonly Label _status = new()
    {
        Dock = DockStyle.Fill,
        ForeColor = UiTheme.StatusFg,
        BackColor = UiTheme.StatusBg,
        Font = UiTheme.Hint,
        TextAlign = ContentAlignment.MiddleLeft,
        Padding = new Padding(12, 0, 12, 0),
        Text = "Sign in under Settings if notes do not load.",
    };
    private List<ProjectInfo> _projects = new();
    private bool _busy;

    public NotesForm(AppSettings settings, LogEaseApiClient api)
    {
        _settings = settings;
        _api = api;
        Text = "LogEase — Sticky notes";
        Width = 980;
        Height = 780;
        MinimumSize = new Size(820, 640);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = UiTheme.Bg;
        Font = UiTheme.Ui;

        var today = DateTime.Today;
        _noteDate.Text = today.ToString("yyyy-MM-dd");
        _to.Text = today.ToString("yyyy-MM-dd");
        _from.Text = today.AddDays(-6).ToString("yyyy-MM-dd");
        StyleDateBox(_noteDate);
        StyleDateBox(_from);
        StyleDateBox(_to);
        StyleMultiline(_body);

        _notesGrid = BuildNotesGrid();
        _draftGrid = BuildDraftGrid();

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(12),
            BackColor = UiTheme.Bg,
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 168)); // add note
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 38));   // notes table
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 110)); // summarize
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 62));   // draft table
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));  // status

        root.Controls.Add(BuildAddCard(), 0, 0);
        root.Controls.Add(BuildNotesCard(), 0, 1);
        root.Controls.Add(BuildSummarizeCard(), 0, 2);
        root.Controls.Add(BuildDraftCard(), 0, 3);

        var statusBar = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.StatusBg, Padding = new Padding(0) };
        statusBar.Controls.Add(_status);
        root.Controls.Add(statusBar, 0, 4);

        Controls.Add(root);
        Load += async (_, _) =>
        {
            await ReloadProjectsAsync();
            await ReloadNotesAsync();
        };
    }

    private Control BuildAddCard()
    {
        var card = MakeCard();
        var title = SectionTitle("Add note");
        var hint = UiTheme.HintLabel("Jot down what you worked on — one note per day is fine.");
        hint.Height = 22;

        var dateRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 36,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = UiTheme.Surface,
        };
        dateRow.Controls.Add(UiTheme.FieldLabel("DATE"));
        dateRow.Controls.Add(_noteDate);

        var bodyLabel = UiTheme.FieldLabel("NOTE");
        bodyLabel.Dock = DockStyle.Top;

        var bodyHost = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(1),
            BackColor = UiTheme.Line,
            MinimumSize = new Size(0, 48),
        };
        var bodyInner = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 6, 4, 6), BackColor = Color.White };
        bodyInner.Controls.Add(_body);
        bodyHost.Controls.Add(bodyInner);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 40,
            FlowDirection = FlowDirection.LeftToRight,
            BackColor = UiTheme.Surface,
            Padding = new Padding(0, 4, 0, 0),
        };
        var save = UiTheme.PrimaryButton("Save note");
        save.Click += async (_, _) => await AddNoteAsync();
        actions.Controls.Add(save);

        card.Controls.Add(bodyHost);
        card.Controls.Add(actions);
        card.Controls.Add(bodyLabel);
        card.Controls.Add(dateRow);
        card.Controls.Add(hint);
        card.Controls.Add(title);
        return card;
    }

    private Control BuildNotesCard()
    {
        var card = MakeCard();
        var title = SectionTitle("Notes in range");
        var range = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 40,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = UiTheme.Surface,
        };
        range.Controls.Add(UiTheme.FieldLabel("FROM"));
        range.Controls.Add(_from);
        range.Controls.Add(UiTheme.FieldLabel("TO"));
        range.Controls.Add(_to);
        var refresh = UiTheme.SecondaryButton("Refresh");
        refresh.Click += async (_, _) => await ReloadNotesAsync();
        range.Controls.Add(refresh);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 40,
            FlowDirection = FlowDirection.LeftToRight,
            BackColor = UiTheme.Surface,
            Padding = new Padding(0, 4, 0, 0),
        };
        var update = UiTheme.SecondaryButton("Update selected");
        update.Click += async (_, _) => await UpdateSelectedNoteAsync();
        var delete = UiTheme.SecondaryButton("Delete selected");
        delete.Click += async (_, _) => await DeleteSelectedNoteAsync();
        actions.Controls.Add(update);
        actions.Controls.Add(delete);

        var gridHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 4, 0, 0), BackColor = UiTheme.Surface };
        gridHost.Controls.Add(_notesGrid);

        card.Controls.Add(gridHost);
        card.Controls.Add(actions);
        card.Controls.Add(range);
        card.Controls.Add(title);
        return card;
    }

    private Control BuildSummarizeCard()
    {
        var card = MakeCard();
        var title = SectionTitle("Summarize → worklog draft");
        var checks = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 32,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = UiTheme.Surface,
        };
        StyleCheck(_includeNotes);
        StyleCheck(_includeGitlab);
        StyleCheck(_includeMail);
        checks.Controls.Add(_includeNotes);
        checks.Controls.Add(_includeGitlab);
        checks.Controls.Add(_includeMail);

        var hint = UiTheme.HintLabel("Configure GitLab / mail under Settings before enabling those sources.");
        hint.Height = 22;

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 40,
            FlowDirection = FlowDirection.LeftToRight,
            BackColor = UiTheme.Surface,
        };
        var draft = UiTheme.PrimaryButton("Summarize → draft");
        draft.Click += async (_, _) => await DraftAsync();
        actions.Controls.Add(draft);

        card.Controls.Add(actions);
        card.Controls.Add(hint);
        card.Controls.Add(checks);
        card.Controls.Add(title);
        return card;
    }

    private Control BuildDraftCard()
    {
        var card = MakeCard();
        var title = SectionTitle("Preview (edit like AI Entry)");
        var hint = UiTheme.HintLabel("Edit date / project / activity / hours / description, then save.");
        hint.Height = 22;

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 40,
            FlowDirection = FlowDirection.LeftToRight,
            BackColor = UiTheme.Surface,
            Padding = new Padding(0, 4, 0, 0),
        };
        var save = UiTheme.PrimaryButton("Save to worklogs");
        save.Click += async (_, _) => await SaveDraftAsync();
        var remove = UiTheme.SecondaryButton("Remove row");
        remove.Click += (_, _) => RemoveSelectedDraftRows();
        actions.Controls.Add(save);
        actions.Controls.Add(remove);

        var gridHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 4, 0, 0), BackColor = UiTheme.Surface };
        gridHost.Controls.Add(_draftGrid);

        card.Controls.Add(gridHost);
        card.Controls.Add(actions);
        card.Controls.Add(hint);
        card.Controls.Add(title);
        return card;
    }

    private DataGridView BuildNotesGrid()
    {
        var grid = BaseGrid();
        grid.DataSource = _notes;
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(StickyNoteInfo.NoteDate),
            HeaderText = "DATE",
            Name = "NoteDate",
            FillWeight = 14,
            MinimumWidth = 100,
            DefaultCellStyle = { WrapMode = DataGridViewTriState.False },
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(StickyNoteInfo.Body),
            HeaderText = "NOTE",
            Name = "Body",
            FillWeight = 86,
            MinimumWidth = 280,
            DefaultCellStyle = { WrapMode = DataGridViewTriState.True },
        });
        grid.CellEndEdit += (_, e) =>
        {
            if (e.RowIndex >= 0)
                BeginInvoke(() => AdjustRowHeight(grid, e.RowIndex));
        };
        grid.DataBindingComplete += (_, _) => AdjustAllRowHeights(grid);
        return grid;
    }

    private DataGridView BuildDraftGrid()
    {
        var grid = BaseGrid();
        grid.DataSource = _draft;
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(ParsedRow.WorkDate),
            HeaderText = "DATE",
            Name = "WorkDate",
            FillWeight = 10,
            MinimumWidth = 88,
        });
        grid.Columns.Add(new DataGridViewComboBoxColumn
        {
            DataPropertyName = nameof(ParsedRow.ProjectName),
            HeaderText = "PROJECT",
            Name = "ProjectName",
            FillWeight = 18,
            MinimumWidth = 130,
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
            ValueType = typeof(decimal),
            DefaultCellStyle =
            {
                Format = "0.##",
                Alignment = DataGridViewContentAlignment.MiddleRight,
                WrapMode = DataGridViewTriState.False,
            },
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(ParsedRow.Description),
            HeaderText = "DESCRIPTION",
            Name = "Description",
            FillWeight = 44,
            MinimumWidth = 220,
            DefaultCellStyle = { WrapMode = DataGridViewTriState.True },
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(ParsedRow.Confidence),
            HeaderText = "TRUST",
            Name = "Confidence",
            FillWeight = 7,
            MinimumWidth = 58,
            ReadOnly = true,
        });

        grid.DataError += (_, e) =>
        {
            e.ThrowException = false;
            try { grid.CancelEdit(); } catch { /* ignore */ }
        };
        grid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (grid.IsCurrentCellDirty && grid.CurrentCell is DataGridViewComboBoxCell)
                grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        grid.CellValueChanged += Grid_CellValueChanged;
        grid.DataBindingComplete += (_, _) =>
        {
            RefreshProjectComboItems();
            AdjustAllRowHeights(grid);
        };
        grid.CellEndEdit += (_, e) =>
        {
            if (e.RowIndex >= 0)
                BeginInvoke(() => AdjustRowHeight(grid, e.RowIndex));
        };
        return grid;
    }

    private void Grid_CellValueChanged(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
        if (e.RowIndex >= _draft.Count) return;
        var col = _draftGrid.Columns[e.ColumnIndex].Name;
        var row = _draft[e.RowIndex];
        if (col == "ProjectName")
        {
            var project = _projects.FirstOrDefault(p => p.Name == row.ProjectName);
            if (project is null) return;
            row.ProjectId = project.Id;
            var act = project.Activities.FirstOrDefault();
            row.ActivityId = act?.Id;
            row.ActivityName = act?.Name ?? "";
            RefreshActivityComboForRow(e.RowIndex);
        }
        else if (col == "ActivityName")
        {
            var project = _projects.FirstOrDefault(p => p.Id == row.ProjectId)
                          ?? _projects.FirstOrDefault(p => p.Name == row.ProjectName);
            var act = project?.Activities.FirstOrDefault(a => a.Name == row.ActivityName);
            if (act is not null)
            {
                row.ActivityId = act.Id;
                row.ProjectId = act.ProjectId;
            }
        }
    }

    private void RefreshProjectComboItems()
    {
        if (_draftGrid.Columns["ProjectName"] is not DataGridViewComboBoxColumn projCol) return;
        var names = _projects.Select(p => p.Name).Distinct().ToList();
        projCol.Items.Clear();
        foreach (var n in names)
            projCol.Items.Add(n);

        for (var i = 0; i < _draft.Count; i++)
            RefreshActivityComboForRow(i);
    }

    private void RefreshActivityComboForRow(int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= _draftGrid.Rows.Count) return;
        if (_draftGrid.Rows[rowIndex].Cells["ActivityName"] is not DataGridViewComboBoxCell cell) return;
        var row = _draft[rowIndex];
        var project = _projects.FirstOrDefault(p => p.Id == row.ProjectId)
                      ?? _projects.FirstOrDefault(p => p.Name == row.ProjectName);
        cell.Items.Clear();
        foreach (var a in project?.Activities ?? Enumerable.Empty<ActivityInfo>())
            cell.Items.Add(a.Name);
        if (!string.IsNullOrEmpty(row.ActivityName) && !cell.Items.Contains(row.ActivityName))
            cell.Items.Add(row.ActivityName);
    }

    private async Task ReloadProjectsAsync()
    {
        try
        {
            EnsureAuth();
            _projects = await _api.GetProjectsAsync(_settings.ApiUrl, _settings.Token!);
            RefreshProjectComboItems();
        }
        catch
        {
            // Status will show from notes load if auth fails.
        }
    }

    private async Task ReloadNotesAsync()
    {
        if (_busy) return;
        _busy = true;
        try
        {
            EnsureAuth();
            var list = await _api.GetStickyNotesAsync(
                _settings.ApiUrl, _settings.Token!, _from.Text.Trim(), _to.Text.Trim());
            _notes.Clear();
            foreach (var n in list)
                _notes.Add(n);
            AdjustAllRowHeights(_notesGrid);
            _status.Text = list.Count == 0
                ? $"No sticky notes between {_from.Text.Trim()} and {_to.Text.Trim()}. Add one above."
                : $"{list.Count} note(s) loaded. Edit cells, then Update selected.";
        }
        catch (Exception ex)
        {
            _status.Text = ex.Message;
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task AddNoteAsync()
    {
        try
        {
            EnsureAuth();
            var body = _body.Text.Trim();
            if (string.IsNullOrEmpty(body))
            {
                MessageBox.Show(this, "Enter a note body first.", "LogEase", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            await _api.CreateStickyNoteAsync(_settings.ApiUrl, _settings.Token!, _noteDate.Text.Trim(), body);
            _body.Clear();
            await ReloadNotesAsync();
            _status.Text = "Note saved.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "LogEase", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private async Task UpdateSelectedNoteAsync()
    {
        if (_notesGrid.CurrentCell is null) return;
        var idx = _notesGrid.CurrentCell.RowIndex;
        if (idx < 0 || idx >= _notes.Count) return;
        try
        {
            EnsureAuth();
            _notesGrid.EndEdit();
            var n = _notes[idx];
            await _api.UpdateStickyNoteAsync(_settings.ApiUrl, _settings.Token!, n.Id, n.NoteDate.Trim(), n.Body.Trim());
            _status.Text = "Note updated.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "LogEase", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            await ReloadNotesAsync();
        }
    }

    private async Task DeleteSelectedNoteAsync()
    {
        if (_notesGrid.CurrentCell is null) return;
        var idx = _notesGrid.CurrentCell.RowIndex;
        if (idx < 0 || idx >= _notes.Count) return;
        try
        {
            EnsureAuth();
            await _api.DeleteStickyNoteAsync(_settings.ApiUrl, _settings.Token!, _notes[idx].Id);
            await ReloadNotesAsync();
            _status.Text = "Note deleted.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "LogEase", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private async Task DraftAsync()
    {
        try
        {
            EnsureAuth();
            _status.Text = "Summarizing…";
            await ReloadProjectsAsync();
            var result = await _api.DraftFromSourcesAsync(
                _settings.ApiUrl,
                _settings.Token!,
                _from.Text.Trim(),
                _to.Text.Trim(),
                _includeNotes.Checked,
                _includeGitlab.Checked,
                _includeMail.Checked);
            _draft.Clear();
            foreach (var r in result.Rows)
                _draft.Add(r);
            RefreshProjectComboItems();
            AdjustAllRowHeights(_draftGrid);
            _status.Text = $"Draft via {result.Parser}. Sources: {result.SourcesUsed}. {result.Warning}".Trim();
        }
        catch (Exception ex)
        {
            _status.Text = ex.Message;
            MessageBox.Show(this, ex.Message, "LogEase", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void RemoveSelectedDraftRows()
    {
        if (_draftGrid.CurrentCell is null) return;
        var idx = _draftGrid.CurrentCell.RowIndex;
        if (idx < 0 || idx >= _draft.Count) return;
        _draft.RemoveAt(idx);
    }

    private async Task SaveDraftAsync()
    {
        try
        {
            EnsureAuth();
            _draftGrid.EndEdit();
            if (_draft.Count == 0)
            {
                MessageBox.Show(this, "No draft rows to save.", "LogEase", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            foreach (var r in _draft)
            {
                if (r.ProjectId is null || r.ActivityId is null)
                    throw new InvalidOperationException("Each row needs a project and activity.");
            }
            var count = await _api.SaveAsync(_settings.ApiUrl, _settings.Token!, _draft.ToList());
            _draft.Clear();
            _status.Text = $"Saved {count} row(s).";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "LogEase", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void EnsureAuth()
    {
        if (string.IsNullOrWhiteSpace(_settings.Token))
            throw new InvalidOperationException("Sign in under Settings first.");
    }

    private static Panel MakeCard()
    {
        var card = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 10),
            Padding = new Padding(12),
            BackColor = UiTheme.Surface,
        };
        card.Paint += (s, e) => UiTheme.PaintCardBorder((Control)s!, e);
        return card;
    }

    private static Label SectionTitle(string text) =>
        new()
        {
            Text = text,
            Dock = DockStyle.Top,
            Height = 28,
            Font = UiTheme.Title,
            ForeColor = UiTheme.Ink,
            TextAlign = ContentAlignment.MiddleLeft,
            BackColor = UiTheme.Surface,
        };

    private static DataGridView BaseGrid()
    {
        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            BackgroundColor = UiTheme.Surface,
            BorderStyle = BorderStyle.None,
            GridColor = UiTheme.Line,
            EnableHeadersVisualStyles = false,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeColumns = true,
            AllowUserToResizeRows = true,
            RowHeadersVisible = true,
            RowHeadersWidth = 28,
            ReadOnly = false,
            EditMode = DataGridViewEditMode.EditOnEnter,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None,
            ShowCellToolTips = true,
            AutoGenerateColumns = false,
            ColumnHeadersHeight = 38,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
            RowTemplate = { Height = 48, MinimumHeight = 40 },
            Font = UiTheme.Ui,
            DefaultCellStyle =
            {
                BackColor = UiTheme.Surface,
                ForeColor = UiTheme.Ink,
                SelectionBackColor = UiTheme.Selection,
                SelectionForeColor = UiTheme.Ink,
                WrapMode = DataGridViewTriState.True,
                Padding = new Padding(4),
                Font = UiTheme.Ui,
            },
            ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = UiTheme.GridHeader,
                ForeColor = UiTheme.Muted,
                Font = UiTheme.Caption,
                Alignment = DataGridViewContentAlignment.MiddleLeft,
                Padding = new Padding(4, 0, 4, 0),
                WrapMode = DataGridViewTriState.False,
            },
        };
        return grid;
    }

    private static void AdjustAllRowHeights(DataGridView grid)
    {
        for (var i = 0; i < grid.Rows.Count; i++)
            AdjustRowHeight(grid, i);
    }

    private static void AdjustRowHeight(DataGridView grid, int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= grid.Rows.Count) return;
        try
        {
            grid.AutoResizeRow(rowIndex, DataGridViewAutoSizeRowMode.AllCellsExceptHeader);
            var row = grid.Rows[rowIndex];
            if (row.Height < 40) row.Height = 40;
            if (row.Height > 160) row.Height = 160;
        }
        catch
        {
            // ignore measurement errors during binding
        }
    }

    private static void StyleDateBox(TextBox t)
    {
        t.Width = 130;
        t.Height = 28;
        t.BorderStyle = BorderStyle.FixedSingle;
        t.Font = UiTheme.Ui;
        t.Margin = new Padding(0, 4, 12, 0);
    }

    private static void StyleMultiline(TextBox t)
    {
        t.Dock = DockStyle.Fill;
        t.BorderStyle = BorderStyle.None;
        t.BackColor = Color.White;
        t.ForeColor = UiTheme.Ink;
        t.Font = UiTheme.Ui;
        t.AcceptsTab = true;
        t.WordWrap = true;
    }

    private static void StyleCheck(CheckBox c)
    {
        c.ForeColor = UiTheme.Ink;
        c.Font = UiTheme.Ui;
        c.Margin = new Padding(0, 6, 16, 0);
        c.BackColor = UiTheme.Surface;
    }
}
