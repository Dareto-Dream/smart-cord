namespace SmartCord.Ui.Pages;

/// <summary>
/// Read-only view of the status-preset library. Presets (and their process-name
/// triggers) live in <c>appsettings.json</c>; this page just shows them and opens
/// the file for editing — the app reloads the change live.
/// </summary>
public sealed class PresetsPage : SmartCordPage
{
    private readonly DataGridView _grid = new();
    private readonly Button _openFile = new() { Text = "Edit appsettings.json", Width = 180, Height = 32 };

    public PresetsPage(SmartCordController controller) : base(controller)
    {
        var root = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Surface, Padding = new Padding(0, 44, 0, 0) };

        var header = Theme.Heading("Status presets");
        header.Location = new Point(0, 0);
        root.Controls.Add(header);

        _grid.Dock = DockStyle.Fill;
        _grid.BackgroundColor = Theme.Surface;
        _grid.BorderStyle = BorderStyle.None;
        _grid.EnableHeadersVisualStyles = false;
        _grid.RowHeadersVisible = false;
        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
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
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Key", FillWeight = 14 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Label", FillWeight = 16 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Details", FillWeight = 26 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Large / small image", FillWeight = 20 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Triggers on process", FillWeight = 24 });

        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 48, BackColor = Theme.Surface, Padding = new Padding(0, 8, 0, 0) };
        Theme.StyleSecondary(_openFile);
        _openFile.Click += (_, _) => OpenAppSettings();
        bar.Controls.Add(_openFile);
        bar.Controls.Add(Theme.Caption("  Presets reload automatically after you save."));

        root.Controls.Add(_grid);
        root.Controls.Add(bar);
        _grid.BringToFront();
        Controls.Add(root);
    }

    public override string Title => "Presets";

    public override void ApplyState()
    {
        _grid.Rows.Clear();
        foreach (var p in Controller.Settings.StatusPresets)
        {
            _grid.Rows.Add(
                p.Key,
                p.Label,
                p.Details,
                $"{Dash(p.LargeImageKey)} / {Dash(p.SmallImageKey)}",
                p.ProcessNames.Count > 0 ? string.Join(", ", p.ProcessNames) : "— (manual only)");
        }
    }

    private static string Dash(string s) => string.IsNullOrWhiteSpace(s) ? "—" : s;

    private void OpenAppSettings()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = path, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Couldn't open {path}\n\n{ex.Message}", "SmartCord", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
