namespace SmartCord.Ui.Pages;

public sealed class DashboardPage : SmartCordPage
{
    private readonly PresenceCardControl _card = new() { Width = 400, Height = 150 };
    private readonly Label _connLabel = new() { AutoSize = true, MaximumSize = new Size(400, 0), Margin = new Padding(2, 4, 0, 0), ForeColor = Theme.TextMuted, Font = Theme.UiFont };
    private readonly CheckBox _enabled = new() { Text = "Rich Presence enabled", AutoSize = true };
    private readonly RadioButton _auto = new() { Text = "Auto-detect from running apps", AutoSize = true };
    private readonly RadioButton _manual = new() { Text = "Pin a status preset", AutoSize = true };
    private readonly RadioButton _custom = new() { Text = "Use custom presence", AutoSize = true };
    private readonly DarkComboBox _presetBox = new() { Width = 260 };
    private readonly DarkComboBox _projectBox = new() { Width = 300 };
    private readonly Label _detected = Theme.Caption("");
    private readonly Button _streaming = new() { Text = "🔴 Streaming / DND" };

    private bool _updating;

    public DashboardPage(SmartCordController controller) : base(controller)
    {
        AutoScroll = false;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Surface,
            ColumnCount = 2,
            RowCount = 2,
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var heading = Theme.Heading("Dashboard");
        root.Controls.Add(heading, 0, 0);
        root.SetColumnSpan(heading, 2);

        // ── left: controls ──────────────────────────────────────────────
        var left = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = Theme.Surface,
        };

        _enabled.ForeColor = Theme.TextPrimary;
        _enabled.Font = Theme.UiFontBold;
        _enabled.Margin = new Padding(0, 0, 0, 8);
        _enabled.CheckedChanged += (_, _) => { if (!_updating) Controller.SetEnabled(_enabled.Checked); };
        left.Controls.Add(_enabled);

        left.Controls.Add(Theme.Subheading("MODE"));
        foreach (var rb in new[] { _auto, _manual, _custom })
        {
            rb.ForeColor = Theme.TextPrimary;
            rb.Margin = new Padding(0, 2, 0, 2);
            left.Controls.Add(rb);
        }
        _auto.CheckedChanged += (_, _) => { if (!_updating && _auto.Checked) Controller.SetAutoDetect(); };
        _custom.CheckedChanged += (_, _) => { if (!_updating && _custom.Checked) Controller.ApplyCustomPresence(Controller.CustomPresence); };
        _manual.CheckedChanged += (_, _) => { if (!_updating && _manual.Checked) ApplySelectedPreset(); };

        Theme.StyleInput(_presetBox);
        _presetBox.Margin = new Padding(22, 4, 0, 10);
        _presetBox.SelectedIndexChanged += (_, _) => { if (!_updating && _manual.Checked) ApplySelectedPreset(); };
        left.Controls.Add(_presetBox);

        left.Controls.Add(Theme.Subheading("ACTIVE PROJECT"));
        Theme.StyleInput(_projectBox);
        _projectBox.Margin = new Padding(0, 4, 0, 10);
        _projectBox.SelectedIndexChanged += (_, _) =>
        {
            if (_updating) return;
            Controller.SetActiveProject((_projectBox.SelectedItem as ProjectRow)?.Project);
        };
        left.Controls.Add(_projectBox);
        left.Controls.Add(_detected);

        left.Controls.Add(Theme.Subheading("QUICK ACTIONS"));
        Theme.StyleSecondary(_streaming);
        _streaming.Margin = new Padding(0, 2, 0, 2);
        _streaming.Click += (_, _) => Controller.SetStreamingMode(!Controller.IsStreaming);
        left.Controls.Add(_streaming);

        // ── right: live card ────────────────────────────────────────────
        var right = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = Theme.Surface,
        };
        right.Controls.Add(Theme.Subheading("WHAT DISCORD SEES"));
        _card.IconsDirectory = Controller.IconsDirectory;
        _card.Margin = new Padding(0, 4, 0, 8);
        right.Controls.Add(_card);
        right.Controls.Add(_connLabel);

        root.Controls.Add(left, 0, 1);
        root.Controls.Add(right, 1, 1);
        Controls.Add(root);
    }

    public override string Title => "Dashboard";

    public override void ApplyState()
    {
        _updating = true;
        try
        {
            _card.IconsDirectory = Controller.IconsDirectory;
            _card.Show(Controller.CurrentPayload, Controller.CurrentStartedAtUtc, Controller.IsEnabled, "Nothing to show");

            _enabled.Checked = Controller.IsEnabled;

            if (!Controller.IsConfigured)
            {
                _connLabel.Text = "⚠  No Discord Client ID — set one in Settings";
                _connLabel.ForeColor = Theme.Red;
            }
            else if (Controller.IsConnected)
            {
                _connLabel.Text = "●  " + Controller.LastStatus;
                _connLabel.ForeColor = Theme.Green;
            }
            else
            {
                _connLabel.Text = "○  " + Controller.LastStatus;
                _connLabel.ForeColor = Theme.Yellow;
            }

            _auto.Checked = Controller.Mode == PresenceMode.AutoDetect;
            _manual.Checked = Controller.Mode == PresenceMode.ManualPreset;
            _custom.Checked = Controller.Mode == PresenceMode.Custom;
            _presetBox.Enabled = Controller.Mode == PresenceMode.ManualPreset;

            RebuildPresetBox();
            RebuildProjectBox();

            _detected.Text = Controller.Mode == PresenceMode.AutoDetect
                ? $"Detected now: {Controller.DetectedActivityText}"
                : $"Auto-detect would show: {Controller.DetectedActivityText}";

            _streaming.Text = Controller.IsStreaming ? "⏹  End streaming / DND" : "🔴 Streaming / DND";
            _streaming.BackColor = Controller.IsStreaming ? Theme.Red : Theme.SurfaceAlt;
        }
        finally
        {
            _updating = false;
        }
    }

    private void RebuildPresetBox()
    {
        _presetBox.Items.Clear();
        foreach (var preset in Controller.Settings.StatusPresets)
        {
            _presetBox.Items.Add(new PresetRow(preset));
        }
        var current = Controller.ManualPreset?.Key;
        for (var i = 0; i < _presetBox.Items.Count; i++)
        {
            if (((PresetRow)_presetBox.Items[i]!).Preset.Key == current)
            {
                _presetBox.SelectedIndex = i;
                return;
            }
        }
        if (_presetBox.Items.Count > 0 && _presetBox.SelectedIndex < 0)
        {
            _presetBox.SelectedIndex = 0;
        }
    }

    private void RebuildProjectBox()
    {
        _projectBox.Items.Clear();
        _projectBox.Items.Add(new ProjectRow(null));
        foreach (var project in Controller.Projects)
        {
            _projectBox.Items.Add(new ProjectRow(project));
        }

        var activeId = Controller.ActiveProject?.Id;
        for (var i = 0; i < _projectBox.Items.Count; i++)
        {
            if (((ProjectRow)_projectBox.Items[i]!).Project?.Id == activeId)
            {
                _projectBox.SelectedIndex = i;
                return;
            }
        }
        _projectBox.SelectedIndex = 0;
    }

    private void ApplySelectedPreset()
    {
        if (_presetBox.SelectedItem is PresetRow row)
        {
            Controller.SetManualPreset(row.Preset);
        }
    }

    private sealed record PresetRow(StatusPreset Preset)
    {
        public override string ToString() => $"{Preset.Label}  ·  {Preset.Details}";
    }

    private sealed record ProjectRow(ProjectItem? Project)
    {
        public override string ToString() => Project is null ? "None" : Project.Title;
    }
}
