namespace LogEase.Desktop;

/// <summary>Clickable header that shows/hides a content panel (settings accordion).</summary>
public sealed class CollapsibleSection : FlowLayoutPanel
{
    private readonly Button _header;
    private readonly FlowLayoutPanel _body;
    private readonly string _title;
    private readonly Action<CollapsibleSection>? _onExpand;
    private bool _expanded;

    private string _headerBase = "";

    public CollapsibleSection(
        string title,
        string? subtitle = null,
        bool expanded = false,
        int width = 540,
        Action<CollapsibleSection>? onExpand = null)
    {
        _title = title;
        _onExpand = onExpand;
        _expanded = expanded;
        _headerBase = string.IsNullOrWhiteSpace(subtitle)
            ? title
            : $"{title}   —   {subtitle}";

        AutoSize = true;
        FlowDirection = FlowDirection.TopDown;
        WrapContents = false;
        Width = width;
        Margin = new Padding(0, 0, 0, 8);
        BackColor = UiTheme.Surface;
        BorderStyle = BorderStyle.FixedSingle;
        Padding = new Padding(0);

        _header = new Button
        {
            FlatStyle = FlatStyle.Flat,
            Height = 42,
            Width = width - 2,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(10, 0, 10, 0),
            Font = UiTheme.UiBold,
            ForeColor = UiTheme.Ink,
            BackColor = UiTheme.GridHeader,
            Cursor = Cursors.Hand,
            Margin = new Padding(0),
            TabStop = true,
        };
        _header.FlatAppearance.BorderSize = 0;
        _header.FlatAppearance.MouseOverBackColor = Color.FromArgb(230, 228, 236);
        _header.Click += (_, _) => Toggle();

        _body = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Visible = expanded,
            Width = width - 2,
            Padding = new Padding(12, 8, 12, 12),
            BackColor = UiTheme.Surface,
            Margin = new Padding(0),
        };

        Controls.Add(_header);
        Controls.Add(_body);
        UpdateHeader();
    }

    public string Title => _title;

    public void AddContent(params Control[] controls)
    {
        foreach (var c in controls)
            _body.Controls.Add(c);
    }

    public void SetExpanded(bool expanded)
    {
        if (_expanded == expanded) return;
        _expanded = expanded;
        _body.Visible = expanded;
        UpdateHeader();
        if (expanded)
            _onExpand?.Invoke(this);
    }

    public void Toggle() => SetExpanded(!_expanded);

    private void UpdateHeader() =>
        _header.Text = (_expanded ? "▼  " : "▶  ") + _headerBase;
}
