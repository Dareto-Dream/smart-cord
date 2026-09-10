namespace SmartCord.Ui.Pages;

public sealed class LogsPage : SmartCordPage
{
    private readonly TextBox _view = new()
    {
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Vertical,
        Dock = DockStyle.Fill,
        WordWrap = false,
        BackColor = Theme.Base,
        ForeColor = Theme.TextMuted,
        Font = new Font("Consolas", 9f),
        BorderStyle = BorderStyle.FixedSingle,
    };

    private readonly Button _refresh = new() { Text = "Refresh", Width = 90, Height = 32 };
    private readonly Button _openFolder = new() { Text = "Open logs folder", Width = 140, Height = 32 };
    private readonly Button _downloadIcons = new() { Text = "Download Discord assets", Width = 180, Height = 32 };
    private readonly CheckBox _auto = new() { Text = "Auto-refresh", AutoSize = true, Checked = true, ForeColor = Theme.TextPrimary };
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 2500 };

    public LogsPage(SmartCordController controller) : base(controller)
    {
        var root = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Surface, Padding = new Padding(0, 44, 0, 0) };

        var header = Theme.Heading("Logs");
        header.Location = new Point(0, 0);
        root.Controls.Add(header);

        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 48, BackColor = Theme.Surface, Padding = new Padding(0, 8, 0, 0) };
        Theme.StyleSecondary(_refresh);
        Theme.StyleSecondary(_openFolder);
        Theme.StyleSecondary(_downloadIcons);
        _refresh.Click += (_, _) => ReloadLog();
        _openFolder.Click += (_, _) => MainForm.OpenPath(AppPaths.LogDirectory);
        _downloadIcons.Click += async (_, _) =>
        {
            _downloadIcons.Enabled = false;
            _downloadIcons.Text = "Downloading…";
            try { await Controller.DownloadIconsAsync(); }
            catch { /* controller already notified */ }
            finally { _downloadIcons.Enabled = true; _downloadIcons.Text = "Download Discord assets"; }
        };
        _auto.Margin = new Padding(10, 8, 0, 0);
        bar.Controls.AddRange([_refresh, _openFolder, _downloadIcons, _auto]);

        root.Controls.Add(_view);
        root.Controls.Add(bar);
        _view.BringToFront();
        Controls.Add(root);

        _timer.Tick += (_, _) => { if (_auto.Checked && Parent is not null) ReloadLog(); };
        _timer.Start();
    }

    public override string Title => "Logs";

    public override void ApplyState()
    {
        // Called by the shell whenever this page is shown — refresh the tail.
        ReloadLog();
    }

    private void ReloadLog()
    {
        try
        {
            var file = FileLoggerProvider.CurrentLogFile;
            if (!File.Exists(file))
            {
                _view.Text = "(no log file yet today)";
                return;
            }

            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            var text = reader.ReadToEnd();

            var lines = text.Split('\n');
            if (lines.Length > 500)
            {
                text = string.Join('\n', lines[^500..]);
            }

            var atBottom = _view.SelectionStart >= _view.TextLength - 2;
            _view.Text = text.Replace("\n", Environment.NewLine);
            if (atBottom)
            {
                _view.SelectionStart = _view.TextLength;
                _view.ScrollToCaret();
            }
        }
        catch (Exception ex)
        {
            _view.Text = $"(couldn't read log: {ex.Message})";
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
        }
        base.Dispose(disposing);
    }
}
