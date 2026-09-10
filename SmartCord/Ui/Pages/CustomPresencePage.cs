namespace SmartCord.Ui.Pages;

public sealed class CustomPresencePage : SmartCordPage
{
    private readonly TextBox _details = new();
    private readonly TextBox _state = new();
    private readonly TextBox _largeKey = new();
    private readonly TextBox _largeText = new();
    private readonly TextBox _smallKey = new();
    private readonly TextBox _smallText = new();
    private readonly TextBox _btn1Label = new();
    private readonly TextBox _btn1Url = new();
    private readonly TextBox _btn2Label = new();
    private readonly TextBox _btn2Url = new();

    private readonly PresenceCardControl _preview = new() { Width = 420 };
    private readonly Button _apply = new() { Text = "Apply custom presence", Width = 190, Height = 34 };
    private readonly Button _revert = new() { Text = "Revert", Width = 90, Height = 34 };

    private bool _updating;

    public CustomPresencePage(SmartCordController controller) : base(controller)
    {
        var stack = Stack();
        stack.Controls.Add(Theme.Heading("Custom presence"));
        stack.Controls.Add(Theme.Caption("Overrides auto-detect and presets. Asset keys must match files uploaded to the Discord Developer Portal (without .png)."));

        _preview.IconsDirectory = Controller.IconsDirectory;
        _preview.Margin = new Padding(0, 12, 0, 10);
        stack.Controls.Add(_preview);

        var bar = new FlowLayoutPanel { AutoSize = true, BackColor = Theme.Surface, Margin = new Padding(0, 0, 0, 10) };
        Theme.StylePrimary(_apply);
        Theme.StyleSecondary(_revert);
        _apply.Click += (_, _) => Controller.ApplyCustomPresence(Collect());
        _revert.Click += (_, _) => ApplyState();
        bar.Controls.Add(_apply);
        bar.Controls.Add(_revert);
        stack.Controls.Add(bar);

        var grid = new TableLayoutPanel
        {
            ColumnCount = 2,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Theme.Surface,
            Margin = new Padding(0, 4, 0, 8),
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 360));

        AddRow(grid, "Details", _details);
        AddRow(grid, "State", _state);
        AddRow(grid, "Large image key", _largeKey);
        AddRow(grid, "Large image text", _largeText);
        AddRow(grid, "Small image key", _smallKey);
        AddRow(grid, "Small image text", _smallText);
        AddRow(grid, "Button 1 label", _btn1Label);
        AddRow(grid, "Button 1 URL", _btn1Url);
        AddRow(grid, "Button 2 label", _btn2Label);
        AddRow(grid, "Button 2 URL", _btn2Url);
        stack.Controls.Add(grid);

        Controls.Add(stack);

        foreach (var tb in new[] { _details, _state, _largeKey, _largeText, _smallKey, _smallText, _btn1Label, _btn1Url, _btn2Label, _btn2Url })
        {
            tb.TextChanged += (_, _) => { if (!_updating) UpdatePreview(); };
        }
    }

    public override string Title => "Custom";

    public override void ApplyState()
    {
        _updating = true;
        try
        {
            var c = Controller.CustomPresence;
            _details.Text = c.Details;
            _state.Text = c.State;
            _largeKey.Text = c.LargeImageKey;
            _largeText.Text = c.LargeImageText;
            _smallKey.Text = c.SmallImageKey;
            _smallText.Text = c.SmallImageText;
            _btn1Label.Text = c.Button1Label;
            _btn1Url.Text = c.Button1Url;
            _btn2Label.Text = c.Button2Label;
            _btn2Url.Text = c.Button2Url;
            _preview.IconsDirectory = Controller.IconsDirectory;
            _apply.Text = Controller.Mode == PresenceMode.Custom ? "Re-apply custom presence" : "Apply custom presence";
        }
        finally
        {
            _updating = false;
        }
        UpdatePreview();
    }

    private void UpdatePreview() =>
        _preview.Show(Collect().ToPayload(Controller.Settings), DateTime.UtcNow, true, "");

    private CustomPresenceSettings Collect() => new()
    {
        Details = _details.Text.Trim(),
        State = _state.Text.Trim(),
        LargeImageKey = _largeKey.Text.Trim(),
        LargeImageText = _largeText.Text.Trim(),
        SmallImageKey = _smallKey.Text.Trim(),
        SmallImageText = _smallText.Text.Trim(),
        Button1Label = _btn1Label.Text.Trim(),
        Button1Url = _btn1Url.Text.Trim(),
        Button2Label = _btn2Label.Text.Trim(),
        Button2Url = _btn2Url.Text.Trim(),
    };

    private void AddRow(TableLayoutPanel grid, string label, TextBox box)
    {
        var row = grid.RowCount++;
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        grid.Controls.Add(new Label
        {
            Text = label,
            ForeColor = Theme.TextMuted,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 0, 20, 0),
        }, 0, row);
        Theme.StyleInput(box);
        box.Dock = DockStyle.Fill;
        box.Margin = new Padding(0, 3, 0, 3);
        grid.Controls.Add(box, 1, row);
    }
}
