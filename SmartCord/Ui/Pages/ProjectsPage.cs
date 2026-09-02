namespace SmartCord.Ui.Pages;

public sealed class ProjectsPage : SmartCordPage
{
    private readonly DataGridView _grid = new();
    private readonly Button _refresh = new() { Text = "Reload from API", Width = 140, Height = 32 };
    private readonly Button _setActive = new() { Text = "Set as active", Width = 120, Height = 32 };
    private readonly Button _openLink = new() { Text = "Open link", Width = 100, Height = 32 };
    private readonly Label _count = Theme.Caption("");

    private List<ProjectItem> _rows = [];

    public ProjectsPage(SmartCordController controller) : base(controller)
    {
        var root = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Surface, Padding = new Padding(0, 44, 0, 0) };

        var header = Theme.Heading("Projects");
        header.Location = new Point(0, 4);
        root.Controls.Add(header);

        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 48,
            FlowDirection = FlowDirection.LeftToRight,
            BackColor = Theme.Surface,
            Padding = new Padding(0, 8, 0, 0),
        };
        Theme.StylePrimary(_refresh);
        Theme.StyleSecondary(_setActive);
        Theme.StyleSecondary(_openLink);
        _refresh.Click += async (_, _) => { _refresh.Enabled = false; await Controller.RefreshProjectsAsync(); _refresh.Enabled = true; };
        _setActive.Click += (_, _) => { if (Selected() is { } p) Controller.SetActiveProject(p); };
        _openLink.Click += (_, _) => OpenSelectedLink();
        bar.Controls.AddRange([_refresh, _setActive, _openLink, _count]);

        ConfigureGrid();

        root.Controls.Add(_grid);
        root.Controls.Add(bar);
        _grid.BringToFront();
        Controls.Add(root);
    }

    public override string Title => "Projects";

    private void ConfigureGrid()
    {
        _grid.Dock = DockStyle.Fill;
        _grid.BackgroundColor = Theme.Surface;
        _grid.BorderStyle = BorderStyle.None;
        _grid.EnableHeadersVisualStyles = false;
        _grid.RowHeadersVisible = false;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.ReadOnly = true;
        _grid.MultiSelect = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.GridColor = Theme.Border;
        _grid.Font = Theme.UiFont;
        _grid.ColumnHeadersHeight = 34;
        _grid.ColumnHeadersDefaultCellStyle.BackColor = Theme.Base;
        _grid.ColumnHeadersDefaultCellStyle.ForeColor = Theme.TextMuted;
        _grid.ColumnHeadersDefaultCellStyle.Font = Theme.UiFontBold;
        _grid.DefaultCellStyle.BackColor = Theme.SurfaceAlt;
        _grid.DefaultCellStyle.ForeColor = Theme.TextPrimary;
        _grid.DefaultCellStyle.SelectionBackColor = Theme.Accent;
        _grid.DefaultCellStyle.SelectionForeColor = Color.White;
        _grid.AlternatingRowsDefaultCellStyle.BackColor = Theme.Surface;

        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "active", HeaderText = "", FillWeight = 8 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "title", HeaderText = "Title", FillWeight = 34 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "category", HeaderText = "Category", FillWeight = 20 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "progress", HeaderText = "Progress", FillWeight = 18 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "tags", HeaderText = "Tags", FillWeight = 26 });

        _grid.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0 && Selected() is { } p) Controller.SetActiveProject(p); };
    }

    public override void ApplyState()
    {
        _rows = Controller.Projects.ToList();
        var selectedId = Selected()?.Id ?? Controller.ActiveProject?.Id;

        _grid.Rows.Clear();
        foreach (var p in _rows)
        {
            var isActive = p.Id == Controller.ActiveProject?.Id;
            var index = _grid.Rows.Add(
                isActive ? "●" : "",
                p.Title,
                string.IsNullOrWhiteSpace(p.Metadata.Category) ? "—" : p.Metadata.Category,
                string.IsNullOrWhiteSpace(p.Progress) ? p.Metadata.Status : p.Progress,
                p.Tags.Count > 0 ? string.Join(", ", p.Tags) : "—");
            _grid.Rows[index].Cells[0].Style.ForeColor = Theme.Green;
            if (p.Id == selectedId)
            {
                _grid.Rows[index].Selected = true;
            }
        }

        _count.Text = _rows.Count == 0
            ? "No projects loaded — check the API URL in Settings, then Reload."
            : $"{_rows.Count} projects · active: {Controller.ActiveProject?.Title ?? "none"}";

        var has = _grid.SelectedRows.Count > 0;
        _setActive.Enabled = has;
        _openLink.Enabled = has;
    }

    private ProjectItem? Selected()
    {
        if (_grid.SelectedRows.Count == 0)
        {
            return null;
        }
        var i = _grid.SelectedRows[0].Index;
        return i >= 0 && i < _rows.Count ? _rows[i] : null;
    }

    private void OpenSelectedLink()
    {
        if (Selected() is not { } p)
        {
            return;
        }

        var url = !string.IsNullOrWhiteSpace(p.Link)
            ? p.Link
            : Controller.Settings.Frontend.ProjectUrlTemplate
                .Replace("{id}", p.Id.ToString(), StringComparison.OrdinalIgnoreCase)
                .Replace("{slug}", ImageAssetDownloader.NormalizeAssetKey(p.Title), StringComparison.OrdinalIgnoreCase);

        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = uri.ToString(), UseShellExecute = true });
            }
            catch { /* best effort */ }
        }
    }
}
