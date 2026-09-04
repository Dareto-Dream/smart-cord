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
    private readonly Dictionary<string, CheckBox> _screenChecks = new(StringComparer.OrdinalIgnoreCase);

    private readonly Button _save = new() { Text = "Save" };
    private readonly Button _pushNow = new() { Text = "Push now" };
    private readonly Button _test = new() { Text = "Test pattern" };
    private readonly Button _blank = new() { Text = "Blank" };
    private readonly Button _reloadCustom = new() { Text = "Reload custom screens" };
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
        stack.Controls.Add(Theme.Caption(
            "Grey = not available right now (auto-hidden). Spectralis port/token auto-read from its settings file. " +
            "Drop your own JSON screen files (see STANDARDS.md) into the custom screens folder — Help menu has a shortcut."));
        stack.Controls.Add(_screenList);

        var bar = new FlowLayoutPanel { AutoSize = true, BackColor = Theme.Surface, Margin = new Padding(0, 8, 0, 4) };
        Theme.StylePrimary(_save);
        Theme.StyleSecondary(_pushNow);
        Theme.StyleSecondary(_test);
        Theme.StyleSecondary(_blank);
        Theme.StyleSecondary(_reloadCustom);
        _save.Click += (_, _) => SaveAll();
        _pushNow.Click += async (_, _) => await Run(_pushNow, () => Controller.Pixoo.PushNowAsync());
        _test.Click += async (_, _) => await Run(_test, () => Controller.Pixoo.TestPatternAsync());
        _blank.Click += async (_, _) => await Run(_blank, () => Controller.Pixoo.BlankAsync());
        _reloadCustom.Click += (_, _) =>
        {
            Controller.Pixoo.ReloadCustomScreens();
            _status.Text = $"Custom screens reloaded {DateTime.Now:HH:mm:ss}";
        };
        bar.Controls.AddRange([_save, _pushNow, _test, _blank, _reloadCustom]);
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

            // Settings reload (e.g. appsettings.local.json getting re-touched by a
            // sync client) can fire ApplyState mid-edit. Don't stomp a field the
            // user is actively typing into, or their edit gets silently reverted
            // before they ever click Save.
            // ContainsFocus, not Focused: NumericUpDown's actual focus lands on its
            // internal child text box, so Focused alone would always read false.
            if (!_host.ContainsFocus) _host.Text = p.Host;
            if (!_brightness.ContainsFocus) _brightness.Value = Clamp(p.Brightness, _brightness);
            if (!_push.ContainsFocus) _push.Value = Clamp(p.PushSeconds, _push);
            if (!_dwell.ContainsFocus) _dwell.Value = Clamp(p.DwellSeconds, _dwell);
            if (!_target.ContainsFocus) _target.Value = Math.Clamp((decimal)p.DailyTargetHours, _target.Minimum, _target.Maximum);

            RebuildScreenChecklist(p);
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

    /// <summary>Rebuilds the checklist only when the set of known screen ids actually
    /// changes (e.g. after "Reload custom screens") — not on every ApplyState tick,
    /// which would flicker and fight the user's in-progress clicks.</summary>
    private void RebuildScreenChecklist(PixooSettings p)
    {
        var statuses = Controller.Pixoo.ScreenStatus();

        if (!statuses.Select(s => s.Id).SequenceEqual(_screenChecks.Keys, StringComparer.OrdinalIgnoreCase))
        {
            _screenList.Controls.Clear();
            _screenChecks.Clear();
            foreach (var s in statuses)
            {
                var cb = new CheckBox { Text = s.Label, AutoSize = true, ForeColor = Theme.TextPrimary, Margin = new Padding(0, 2, 0, 2) };
                _screenChecks[s.Id] = cb;
                _screenList.Controls.Add(cb);
            }
        }

        foreach (var s in statuses)
        {
            var cb = _screenChecks[s.Id];
            cb.Checked = p.Screens.Contains(s.Id, StringComparer.OrdinalIgnoreCase);
            cb.ForeColor = s.Available ? Theme.TextPrimary : Theme.TextFaint;
        }
    }

    private void SaveAll()
    {
        var chosen = _screenChecks.Where(kv => kv.Value.Checked).Select(kv => kv.Key).ToArray();

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

    private void SaveField(string key, bool value)
    {
        // Typed bool, not object: JsonValue.Create<object>(boxedBool) needs a
        // TypeInfoResolver System.Text.Json won't supply by default and throws.
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
