using SmartCord.Integrations;

namespace SmartCord.Ui.Pages;

public sealed class IntegrationsPage : SmartCordPage
{
    private readonly CheckBox _drivePresence = new()
    {
        Text = "Let a live WakaTime / Hackatime heartbeat drive presence",
        AutoSize = true,
        ForeColor = Theme.TextPrimary,
        Font = Theme.UiFontBold,
    };
    private readonly Label _current = Theme.Caption("");
    private readonly List<ConnectorCard> _cards = [];

    public IntegrationsPage(SmartCordController controller) : base(controller)
    {
        var stack = Stack();
        stack.Controls.Add(Theme.Heading("Integrations"));
        stack.Controls.Add(Theme.Caption(
            "Connect a time-tracker so \"Coding in <language>\" and today's hours show automatically. " +
            "Falls back to app-detection / presets when there's no recent activity."));

        _drivePresence.Margin = new Padding(0, 10, 0, 4);
        _drivePresence.CheckedChanged += (_, _) => Controller.CodingActivityDrivesPresence = _drivePresence.Checked;
        stack.Controls.Add(_drivePresence);
        stack.Controls.Add(_current);

        foreach (var source in Controller.Integrations.Sources)
        {
            var card = new ConnectorCard(source, Controller);
            card.Margin = new Padding(0, 14, 0, 0);
            _cards.Add(card);
            stack.Controls.Add(card);
        }

        Controls.Add(stack);
    }

    public override string Title => "Integrations";

    public override void ApplyState()
    {
        _drivePresence.Checked = Controller.CodingActivityDrivesPresence;

        var snap = Controller.DrivingCodingActivity;
        _current.Text = snap is not null
            ? $"Now driving presence: {snap.DetailsLine} — {snap.StateLine}  (via {snap.Source})"
            : Controller.Integrations.AnyConnected
                ? "No recent coding activity — presence is on app-detection / presets."
                : "Nothing connected yet.";

        foreach (var card in _cards)
        {
            card.ApplyState();
        }
    }

    // ── per-service card ───────────────────────────────────────────────

    private sealed class ConnectorCard : FlowLayoutPanel
    {
        private readonly CodingActivitySource _source;
        private readonly SmartCordController _controller;

        private readonly Label _status = new() { AutoSize = true, Font = Theme.UiFontBold };
        private readonly Label _dot = new() { Text = "●", AutoSize = true, Font = Theme.UiFontBold };
        private readonly Label _now = Theme.Caption("");
        private readonly FlowLayoutPanel _pending = new() { AutoSize = true, BackColor = Theme.SurfaceAlt, Visible = false, Padding = new Padding(8, 4, 8, 4), Margin = new Padding(0, 4, 0, 4) };
        private readonly Label _pendingLabel = new() { AutoSize = true, ForeColor = Theme.TextMuted, Margin = new Padding(0, 6, 12, 0) };
        private readonly Button _finish = new() { Text = "Finish connecting" };

        private readonly TextBox _clientId = new() { PlaceholderText = "Client ID", Width = 200 };
        private readonly TextBox _clientSecret = new() { PlaceholderText = "Client secret", Width = 200, UseSystemPasswordChar = true };
        private readonly Button _connect = new() { Text = "Connect" };
        private readonly Button _disconnect = new() { Text = "Disconnect OAuth" };

        private readonly TextBox _apiKey = new() { PlaceholderText = "Personal API key", Width = 260, UseSystemPasswordChar = true };
        private readonly Button _saveKey = new() { Text = "Save key" };
        private readonly Button _clearKey = new() { Text = "Clear key" };

        public ConnectorCard(CodingActivitySource source, SmartCordController controller)
        {
            _source = source;
            _controller = controller;

            FlowDirection = FlowDirection.TopDown;
            WrapContents = false;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            BackColor = Theme.Card;
            Padding = new Padding(16);
            MinimumSize = new Size(640, 0);

            var stack = this;

            var titleRow = new FlowLayoutPanel { AutoSize = true, BackColor = Theme.Card, Margin = new Padding(0) };
            var title = new Label { Text = source.DisplayName, AutoSize = true, Font = Theme.SubheadingFont, ForeColor = Theme.TextPrimary, Margin = new Padding(0, 0, 8, 0) };
            _dot.Margin = new Padding(0, 3, 6, 0);
            _status.ForeColor = Theme.TextMuted;
            _status.Margin = new Padding(0, 2, 0, 0);
            titleRow.Controls.AddRange([title, _dot, _status]);
            stack.Controls.Add(titleRow);

            _now.Margin = new Padding(0, 2, 0, 6);
            stack.Controls.Add(_now);

            stack.Controls.Add(Theme.Caption(source.SetupHint));
            var redirect = new TextBox
            {
                Text = "Redirect URI:  " + source.RedirectUri,
                ReadOnly = true,
                Width = 400,
                BackColor = Theme.SurfaceAlt,
                ForeColor = Theme.TextMuted,
                BorderStyle = BorderStyle.FixedSingle,
                Margin = new Padding(0, 4, 0, 8),
            };
            stack.Controls.Add(redirect);

            // pending strip
            _finish.Click += async (_, _) => await RunAsync(_finish, "Waiting for browser…", () => _source.FinishPendingOAuthAsync(CancellationToken.None));
            Theme.StylePrimary(_finish);
            _pending.Controls.Add(_pendingLabel);
            _pending.Controls.Add(_finish);

            // OAuth row
            var oauthRow = new FlowLayoutPanel { AutoSize = true, BackColor = Theme.Card, Margin = new Padding(0, 2, 0, 6) };
            Theme.StyleInput(_clientId);
            Theme.StyleInput(_clientSecret);
            Theme.StylePrimary(_connect);
            Theme.StyleSecondary(_disconnect);
            _connect.Click += async (_, _) => await ConnectAsync();
            _disconnect.Click += (_, _) => { _source.DisconnectOAuth(); AfterChange(); };
            oauthRow.Controls.AddRange([_clientId, _clientSecret, _connect, _disconnect]);

            // API key row
            var keyRow = new FlowLayoutPanel { AutoSize = true, BackColor = Theme.Card, Margin = new Padding(0, 2, 0, 0) };
            Theme.StyleInput(_apiKey);
            Theme.StyleSecondary(_saveKey);
            Theme.StyleSecondary(_clearKey);
            _saveKey.Click += (_, _) => { _source.SetApiKey(_apiKey.Text); _apiKey.Clear(); AfterChange(); };
            _clearKey.Click += (_, _) => { _source.SetApiKey(null); AfterChange(); };
            keyRow.Controls.AddRange([new Label { Text = "or", AutoSize = true, ForeColor = Theme.TextFaint, Margin = new Padding(0, 8, 8, 0) }, _apiKey, _saveKey, _clearKey]);

            stack.Controls.Add(_pending);
            stack.Controls.Add(oauthRow);
            stack.Controls.Add(keyRow);
        }

        public void ApplyState()
        {
            var kind = _source.Connection;
            _dot.ForeColor = _source.IsConnected ? Theme.Green : (kind == ConnectionKind.OAuthPending ? Theme.Yellow : Theme.TextFaint);
            _status.Text = _source.StatusLine;

            var latest = _source.Latest;
            _now.Text = latest is { ActiveNow: true }
                ? $"Now: {Describe(latest)}"
                : latest is not null
                    ? $"Last seen: {Describe(latest)} (idle)"
                    : "No activity reported yet.";

            _pending.Visible = _source.PendingClientId is not null;
            if (_source.PendingClientId is { } pid)
            {
                _pendingLabel.Text = $"Client credentials saved ({pid}) — approve in the browser to finish.";
            }

            _disconnect.Visible = kind is ConnectionKind.OAuth or ConnectionKind.OAuthPending;
            _clearKey.Visible = kind == ConnectionKind.ApiKey;
        }

        private static string Describe(CodingSnapshot s)
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(s.Project)) parts.Add(s.Project!);
            if (!string.IsNullOrWhiteSpace(s.Language)) parts.Add(s.Language!);
            if (s.SecondsToday >= 60) parts.Add($"{s.TodayText} today");
            return parts.Count > 0 ? string.Join(" · ", parts) : "coding";
        }

        private async Task ConnectAsync()
        {
            if (string.IsNullOrWhiteSpace(_clientId.Text) || string.IsNullOrWhiteSpace(_clientSecret.Text))
            {
                MessageBox.Show("Enter both the client ID and client secret.", "SmartCord", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            var id = _clientId.Text.Trim();
            var secret = _clientSecret.Text.Trim();
            await RunAsync(_connect, "Waiting for browser…", () => _source.ConnectOAuthAsync(id, secret, CancellationToken.None));
            _clientSecret.Clear();
        }

        private async Task RunAsync(Button button, string busyText, Func<Task> action)
        {
            var original = button.Text;
            button.Enabled = false;
            button.Text = busyText;
            try
            {
                await action();
                await _controller.Integrations.RefreshAllAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, $"{_source.DisplayName} connection failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                button.Enabled = true;
                button.Text = original;
                AfterChange();
            }
        }

        private void AfterChange()
        {
            ApplyState();
            _ = _controller.ForceRefreshAsync();
        }
    }
}
