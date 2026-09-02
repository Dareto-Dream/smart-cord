using System.Text.Json.Nodes;

namespace SmartCord.Ui.Pages;

public sealed class PixooPage : SmartCordPage
{
    private readonly CheckBox _enabled = new()
    {
        Text = "Push a rotating dashboard to my Pixoo64",
        AutoSize = true,
        ForeColor = Theme.TextPrimary,
        Font = Theme.UiFontBold,
    };
    private readonly TextBox _host = new() { Width = 160 };
    private readonly DarkNumericUpDown _brightness = new() { Minimum = 1, Maximum = 100 };
    private readonly DarkNumericUpDown _push = new() { Minimum = 1, Maximum = 60 };
    private readonly DarkNumericUpDown _dwell = new() { Minimum = 3, Maximum = 120 };
    private readonly DarkNumericUpDown _target = new() { Minimum = 1, Maximum = 16, DecimalPlaces = 1, Increment = 0.5m };

    private readonly FlowLayoutPanel _screenList = new()
    {
        FlowDirection = FlowDirection.TopDown,
        WrapContents = false,
        AutoSize = true,
        BackColor = Theme.Surface,
        Margin = new Padding(0, 2, 0, 6),
    };
    private readonly List<CheckBox> _screenChecks = [];

    private readonly Button _save = new() { Text = "Save" };
    private readonly Button _pushNow = new() { Text = "Push now" };
    private readonly Button _test = new() { Text = "Test pattern" };
    private readonly Button _blank = new() { Text = "Blank" };
    private readonly Button _prev = new() { Text = "◀" };
    private readonly Button _next = new() { Text = "▶" };
    private readonly Button _auto = new() { Text = "Auto" };
    private readonly Label _status = Theme.Caption("");
    private readonly Label _screenLabel = Theme.Caption("");

    private const int PreviewScale = 3;
    private readonly PictureBox _preview = new()
    {
        Width = 64 * PreviewScale,
        Height = 64 * PreviewScale,
        BorderStyle = BorderStyle.FixedSingle,
        BackColor = Color.Black,
        Margin = new Padding(0, 4, 0, 6),
    };
    private readonly System.Windows.Forms.Timer _previewTimer = new() { Interval = 600 };

    private static readonly (string Id, string Label)[] KnownScreens =
    [
        ("status", "SmartCord status"),
        ("nowplaying", "Spectralis now playing"),
        ("compute", "AI compute (training / inference)"),
        ("gpu", "GPU dashboard"),
        ("system", "System (CPU / RAM)"),
    ];

    private bool _updating;

    public PixooPage(SmartCordController controller) : base(controller)
    {
        AutoScroll = false;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Surface,
            ColumnCount = 2,
            RowCount = 2,
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var header = Theme.Heading("Pixoo64");
        root.Controls.Add(header, 0, 0);
        root.SetColumnSpan(header, 2);

        // right column: live preview + rotation controls
        var right = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Theme.Surface, Padding = new Padding(20, 0, 0, 0) };
        right.Controls.Add(Theme.Subheading("ON THE DEVICE"));
        right.Controls.Add(_preview);
        var flip = new FlowLayoutPanel { AutoSize = true, BackColor = Theme.Surface, Margin = new Padding(0, 0, 0, 4) };
        foreach (var b in new[] { _prev, _next, _auto })
        {
            Theme.StyleSecondary(b);
        }
        _prev.Click += async (_, _) => { Controller.Pixoo.PrevScreen(); await Task.CompletedTask; };
        _next.Click += (_, _) => Controller.Pixoo.NextScreen();
        _auto.Click += (_, _) => Controller.Pixoo.ClearPin();
        flip.Controls.AddRange([_prev, _next, _auto]);
        right.Controls.Add(flip);
        right.Controls.Add(_screenLabel);
        root.Controls.Add(right, 1, 1);

        // left column
        var stack = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, BackColor = Theme.Surface };

        _enabled.Margin = new Padding(0, 4, 0, 8);
        _enabled.CheckedChanged += (_, _) => { if (!_updating) SaveField("Enabled", _enabled.Checked); };
        stack.Controls.Add(_enabled);

        var grid = FieldGrid();
        Row(grid, "Device IP / host", _host);
        Row(grid, "Brightness (0-100)", _brightness);
        Row(grid, "Min push interval (s)", _push);
        Row(grid, "Screen dwell (s)", _dwell);
        Row(grid, "Daily target (hours)", _target);
        stack.Controls.Add(grid);

        stack.Controls.Add(Theme.Subheading("SCREENS IN ROTATION"));
        stack.Controls.Add(Theme.Caption("Grey = not available right now (auto-hidden). Spectralis port/token auto-read from its settings file."));
        foreach (var (id, label) in KnownScreens)
        {
            var cb = new CheckBox { Text = label, AutoSize = true, ForeColor = Theme.TextPrimary, Tag = id, Margin = new Padding(0, 2, 0, 2) };
            _screenChecks.Add(cb);
            _screenList.Controls.Add(cb);
        }
        stack.Controls.Add(_screenList);

        var bar = new FlowLayoutPanel { AutoSize = true, BackColor = Theme.Surface, Margin = new Padding(0, 8, 0, 4) };
        Theme.StylePrimary(_save);
        Theme.StyleSecondary(_pushNow);
        Theme.StyleSecondary(_test);
        Theme.StyleSecondary(_blank);
        _save.Click += (_, _) => SaveAll();
        _pushNow.Click += async (_, _) => await Run(_pushNow, () => Controller.Pixoo.PushNowAsync());
        _test.Click += async (_, _) => await Run(_test, () => Controller.Pixoo.TestPatternAsync());
        _blank.Click += async (_, _) => await Run(_blank, () => Controller.Pixoo.BlankAsync());
        bar.Controls.AddRange([_save, _pushNow, _test, _blank]);
        stack.Controls.Add(bar);
        stack.Controls.Add(_status);

        root.Controls.Add(stack, 0, 1);
        Controls.Add(root);

        Controller.Pixoo.Changed += OnPixooChanged;
        _previewTimer.Tick += (_, _) => RefreshPreview();
        _previewTimer.Start();
    }

    public override string Title => "Pixoo64";

    public override void ApplyState()
    {
        _updating = true;
        try
        {
            var p = Controller.Settings.Pixoo;
            _enabled.Checked = p.Enabled;
            _host.Text = p.Host;
            _brightness.Value = Clamp(p.Brightness, _brightness);
            _push.Value = Clamp(p.PushSeconds, _push);
            _dwell.Value = Clamp(p.DwellSeconds, _dwell);
            _target.Value = Math.Clamp((decimal)p.DailyTargetHours, _target.Minimum, _target.Maximum);

            var statuses = Controller.Pixoo.ScreenStatus().ToDictionary(s => s.Id, s => s, StringComparer.OrdinalIgnoreCase);
            foreach (var cb in _screenChecks)
            {
                var id = (string)cb.Tag!;
                cb.Checked = p.Screens.Contains(id, StringComparer.OrdinalIgnoreCase);
                var available = statuses.TryGetValue(id, out var st) && st.Available;
                cb.ForeColor = available ? Theme.TextPrimary : Theme.TextFaint;
            }
        }
        finally
        {
            _updating = false;
        }
        UpdateStatusLabels();
        RefreshPreview();
    }

    private void OnPixooChanged()
    {
        if (IsDisposed) return;
        if (InvokeRequired) { BeginInvoke(OnPixooChanged); return; }
        UpdateStatusLabels();
        RefreshPreview();
    }

    private void UpdateStatusLabels()
    {
        _status.Text = Controller.Pixoo.LastResult;
        var pinned = Controller.Pixoo.PinnedId;
        _screenLabel.Text = pinned is not null
            ? $"pinned: {pinned}  (Auto to resume)"
            : $"rotating · {Controller.Pixoo.CurrentScreenId}";
    }

    private static int Clamp(int value, NumericUpDown box) =>
        (int)Math.Clamp(value, box.Minimum, box.Maximum);

    private void RefreshPreview()
    {
        if (!Visible) return;
        try
        {
            var old = _preview.Image;
            _preview.Image = Controller.Pixoo.Canvas.ToBitmap(PreviewScale);
            old?.Dispose();
        }
        catch { /* best effort */ }
    }

    private void SaveAll()
    {
        var chosen = KnownScreens
            .Where(k => _screenChecks.First(cb => (string)cb.Tag! == k.Id).Checked)
            .Select(k => k.Id)
            .ToArray();

        SettingsWriter.Patch(root =>
        {
            var p = root.Section("Pixoo");
            p["Enabled"] = _enabled.Checked;
            p["Host"] = _host.Text.Trim();
            p["Brightness"] = (int)_brightness.Value;
            p["PushSeconds"] = (int)_push.Value;
            p["DwellSeconds"] = (int)_dwell.Value;
            p["DailyTargetHours"] = (double)_target.Value;
            var arr = new JsonArray();
            foreach (var id in chosen)
            {
                arr.Add(id);
            }
            p["Screens"] = arr;
        });
        _status.Text = $"Saved {DateTime.Now:HH:mm:ss} — reloading…";
    }

    private void SaveField(string key, object value)
    {
        SettingsWriter.Patch(root => root.Section("Pixoo")[key] = JsonValue.Create(value));
    }

    private async Task Run(Button button, Func<Task> action)
    {
        button.Enabled = false;
        try { await action(); }
        catch (Exception ex) { _status.Text = ex.Message; }
        finally { button.Enabled = true; }
    }

    private static TableLayoutPanel FieldGrid()
    {
        var grid = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = Theme.Surface, Margin = new Padding(0, 2, 0, 6) };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 200));
        return grid;
    }

    private void Row(TableLayoutPanel grid, string label, Control input)
    {
        var row = grid.RowCount++;
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        grid.Controls.Add(new Label { Text = label, ForeColor = Theme.TextMuted, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 20, 0) }, 0, row);
        Theme.StyleInput(input);
        input.Dock = DockStyle.Fill;
        input.Margin = new Padding(0, 3, 0, 3);
        grid.Controls.Add(input, 1, row);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Controller.Pixoo.Changed -= OnPixooChanged;
            _previewTimer.Dispose();
            _preview.Image?.Dispose();
        }
        base.Dispose(disposing);
    }
}
