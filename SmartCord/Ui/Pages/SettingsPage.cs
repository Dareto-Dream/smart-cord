using System.Text.Json.Nodes;

namespace SmartCord.Ui.Pages;

public sealed class SettingsPage : SmartCordPage
{
    private readonly TextBox _clientId = new();
    private readonly TextBox _appName = new();
    private readonly TextBox _fallbackState = new();
    private readonly DarkNumericUpDown _poll = new() { Minimum = 3, Maximum = 600 };
    private readonly DarkNumericUpDown _idle = new() { Minimum = 1, Maximum = 240 };
    private readonly DarkNumericUpDown _away = new() { Minimum = 1, Maximum = 480 };
    private readonly TextBox _largeKey = new();
    private readonly TextBox _smallKey = new();
    private readonly CheckBox _clearOnLock = new() { Text = "Clear presence while the session is locked", AutoSize = true };
    private readonly TextBox _projectsUrl = new();
    private readonly TextBox _buttonUrl = new();
    private readonly TextBox _urlTemplate = new();

    private readonly TextBox _wakaApiUrl = new();
    private readonly DarkNumericUpDown _integrationPoll = new() { Minimum = 30, Maximum = 900 };
    private readonly DarkComboBox _preferredSource = new() { Width = 200 };

    private readonly CheckBox _autoStart = new() { Text = "Start SmartCord when I sign in to Windows", AutoSize = true };
    private readonly CheckBox _minimizeToTray = new() { Text = "Closing the window hides it to the tray", AutoSize = true };

    private readonly CheckBox _localApiEnabled = new() { Text = "Serve current status on localhost (for an OBS browser source)", AutoSize = true };
    private readonly DarkNumericUpDown _localApiPort = new() { Minimum = 1024, Maximum = 65535 };
    private readonly Label _localApiUrl = Theme.Caption("");

    private readonly Button _save = new() { Text = "Save settings", Width = 150, Height = 34 };
    private readonly Label _saved = Theme.Caption("");

    private bool _updating;

    public SettingsPage(SmartCordController controller) : base(controller)
    {
        var stack = Stack();
        stack.Controls.Add(Theme.Heading("Settings"));
        stack.Controls.Add(Theme.Caption("Saved to appsettings.local.json — layered over appsettings.json, applied live (no restart)."));

        stack.Controls.Add(Theme.Subheading("DISCORD"));
        var grid = FieldGrid();
        Row(grid, "Client ID", _clientId);
        Row(grid, "Application name", _appName);
        stack.Controls.Add(grid);

        stack.Controls.Add(Theme.Subheading("PRESENCE"));
        var g2 = FieldGrid();
        Row(g2, "Fallback state text", _fallbackState);
        Row(g2, "Poll interval (seconds)", _poll);
        Row(g2, "Idle after (minutes)", _idle);
        Row(g2, "Away after (minutes)", _away);
        Row(g2, "Default large image key", _largeKey);
        Row(g2, "Default small image key", _smallKey);
        stack.Controls.Add(g2);
        _clearOnLock.ForeColor = Theme.TextPrimary;
        _clearOnLock.Margin = new Padding(0, 6, 0, 0);
        stack.Controls.Add(_clearOnLock);

        stack.Controls.Add(Theme.Subheading("API & LINKS"));
        var g3 = FieldGrid();
        Row(g3, "Projects API URL", _projectsUrl);
        Row(g3, "Fallback button URL", _buttonUrl);
        Row(g3, "Project link template", _urlTemplate);
        stack.Controls.Add(g3);

        stack.Controls.Add(Theme.Subheading("INTEGRATIONS"));
        var g4 = FieldGrid();
        Row(g4, "Poll interval (seconds)", _integrationPoll);
        _preferredSource.Items.AddRange(["Auto (most hours)", "WakaTime", "Hackatime"]);
        Row(g4, "Prefer source", _preferredSource);
        Row(g4, "WakaTime API URL", _wakaApiUrl);
        stack.Controls.Add(g4);
        stack.Controls.Add(Theme.Caption("Connect the services on the Integrations page. WakaTime API URL can point at a self-hosted Wakapi."));

        stack.Controls.Add(Theme.Subheading("APP"));
        foreach (var cb in new[] { _autoStart, _minimizeToTray })
        {
            cb.ForeColor = Theme.TextPrimary;
            cb.Margin = new Padding(0, 4, 0, 4);
            stack.Controls.Add(cb);
        }
        _autoStart.CheckedChanged += (_, _) => { if (!_updating) Controller.AutoStartEnabled = _autoStart.Checked; };
        _minimizeToTray.CheckedChanged += (_, _) => { if (!_updating) Controller.MinimizeToTray = _minimizeToTray.Checked; };

        stack.Controls.Add(Theme.Subheading("LOCAL STATUS SERVER (OBS)"));
        _localApiEnabled.ForeColor = Theme.TextPrimary;
        _localApiEnabled.Margin = new Padding(0, 4, 0, 4);
        stack.Controls.Add(_localApiEnabled);
        var g5 = FieldGrid();
        Row(g5, "Port", _localApiPort);
        stack.Controls.Add(g5);
        stack.Controls.Add(_localApiUrl);
        stack.Controls.Add(Theme.Caption("Point an OBS Browser Source at the overlay URL above. Loopback-only — never reachable off this machine."));

        var bar = new FlowLayoutPanel { AutoSize = true, BackColor = Theme.Surface, Margin = new Padding(0, 14, 0, 0) };
        Theme.StylePrimary(_save);
        _save.Click += (_, _) => Save();
        bar.Controls.Add(_save);
        bar.Controls.Add(_saved);
        stack.Controls.Add(bar);

        Controls.Add(stack);
    }

    public override string Title => "Settings";

    public override void ApplyState()
    {
        _updating = true;
        try
        {
            var s = Controller.Settings;
            _clientId.Text = s.Discord.ClientId;
            _appName.Text = s.Discord.ApplicationName;
            _fallbackState.Text = s.Presence.FallbackState;
            _poll.Value = Clamp(s.Presence.PollSeconds, _poll);
            _idle.Value = Clamp(s.Presence.IdleAfterMinutes, _idle);
            _away.Value = Clamp(s.Presence.AwayAfterMinutes, _away);
            _largeKey.Text = s.Presence.DefaultLargeImageKey;
            _smallKey.Text = s.Presence.DefaultSmallImageKey;
            _clearOnLock.Checked = s.Presence.ClearOnLock;
            _projectsUrl.Text = s.Api.ProjectsUrl;
            _buttonUrl.Text = s.Api.ButtonUrl;
            _urlTemplate.Text = s.Frontend.ProjectUrlTemplate;
            _wakaApiUrl.Text = s.Integrations.WakaTimeApiUrl;
            _integrationPoll.Value = Clamp(s.Integrations.PollSeconds, _integrationPoll);
            _preferredSource.SelectedIndex = s.Integrations.PreferredSource.ToLowerInvariant() switch
            {
                "wakatime" => 1,
                "hackatime" => 2,
                _ => 0,
            };
            _autoStart.Checked = Controller.AutoStartEnabled;
            _minimizeToTray.Checked = Controller.MinimizeToTray;

            _localApiEnabled.Checked = s.LocalApi.Enabled;
            _localApiPort.Value = Clamp(s.LocalApi.Port, _localApiPort);
            _localApiUrl.Text = s.LocalApi.Enabled
                ? $"Overlay: {Controller.LocalApi.OverlayUrl}   ·   JSON: {Controller.LocalApi.StatusUrl}" +
                  (Controller.LocalApi.IsRunning ? "" : Controller.LocalApi.LastError is { } err ? $"  ⚠ {err}" : "  (starting…)")
                : "Disabled";
        }
        finally
        {
            _updating = false;
        }
    }

    private void Save()
    {
        try
        {
            SettingsWriter.Patch(root =>
            {
                var discord = root.Section("Discord");
                discord["ClientId"] = _clientId.Text.Trim();
                discord["ApplicationName"] = _appName.Text.Trim();

                var presence = root.Section("Presence");
                presence["FallbackState"] = _fallbackState.Text.Trim();
                presence["PollSeconds"] = (int)_poll.Value;
                presence["IdleAfterMinutes"] = (int)_idle.Value;
                presence["AwayAfterMinutes"] = (int)_away.Value;
                presence["DefaultLargeImageKey"] = _largeKey.Text.Trim();
                presence["DefaultSmallImageKey"] = _smallKey.Text.Trim();
                presence["ClearOnLock"] = _clearOnLock.Checked;

                var api = root.Section("Api");
                api["ProjectsUrl"] = _projectsUrl.Text.Trim();
                api["ButtonUrl"] = _buttonUrl.Text.Trim();

                root.Section("Frontend")["ProjectUrlTemplate"] = _urlTemplate.Text.Trim();

                var integrations = root.Section("Integrations");
                integrations["WakaTimeApiUrl"] = string.IsNullOrWhiteSpace(_wakaApiUrl.Text)
                    ? "https://wakatime.com/api/v1"
                    : _wakaApiUrl.Text.Trim();
                integrations["PollSeconds"] = (int)_integrationPoll.Value;
                integrations["PreferredSource"] = _preferredSource.SelectedIndex switch
                {
                    1 => "wakatime",
                    2 => "hackatime",
                    _ => "",
                };

                var localApi = root.Section("LocalApi");
                localApi["Enabled"] = _localApiEnabled.Checked;
                localApi["Port"] = (int)_localApiPort.Value;
            });

            _saved.Text = $"  Saved {DateTime.Now:HH:mm:ss} — reloading…";
        }
        catch (Exception ex)
        {
            _saved.Text = $"  Save failed: {ex.Message}";
        }
    }

    private static decimal Clamp(int value, NumericUpDown box) =>
        Math.Clamp(value, box.Minimum, box.Maximum);

    private static TableLayoutPanel FieldGrid()
    {
        var grid = new TableLayoutPanel
        {
            ColumnCount = 2,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Theme.Surface,
            Margin = new Padding(0, 2, 0, 6),
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 360));
        return grid;
    }

    private void Row(TableLayoutPanel grid, string label, Control input)
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

        Theme.StyleInput(input);
        input.Dock = DockStyle.Fill;
        input.Margin = new Padding(0, 3, 0, 3);
        if (input is NumericUpDown n)
        {
            n.BackColor = Theme.SurfaceAlt;
            n.ForeColor = Theme.TextPrimary;
        }
        grid.Controls.Add(input, 1, row);
    }
}
