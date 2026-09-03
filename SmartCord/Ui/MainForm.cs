using SmartCord.Ui.Pages;

namespace SmartCord.Ui;

/// <summary>
/// The actual application window. A coloured sidebar of nav buttons on the left, a
/// swappable content page on the right, a live connection readout at the bottom.
/// Closing it hides to the tray unless the user turned that off (or the tray asks
/// for a real exit).
/// </summary>
public sealed class MainForm : Form
{
    private readonly SmartCordController _controller;
    private readonly Panel _sidebar = new();
    private readonly Panel _content = new();
    private readonly Label _statusDot = new();
    private readonly Label _statusText = new();
    private readonly List<(Button button, SmartCordPage page)> _nav = [];

    private SmartCordPage? _activePage;
    private bool _exiting;

    /// <summary>Set by the tray shell so "Exit" really closes.</summary>
    public bool AllowExit { get; set; }

    /// <summary>Raised when the user picks Exit from the window's File menu / the tray.</summary>
    public event Action? ExitRequested;

    public MainForm(SmartCordController controller)
    {
        _controller = controller;

        // Per-monitor-v2 scales the whole control tree by (current DPI / 96) instead of
        // leaving every hardcoded pixel value (sidebar width, button heights, the card...)
        // correct only at 100% scaling. Without this, a high-DPI or oddly-scaled display
        // (a 2560x1600 panel is rarely run at plain 96 DPI) renders text that outgrows its
        // container while everything sized in raw pixels stays put — that's the clipping.
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96f, 96f);

        Text = "SmartCord";
        BackColor = Theme.Base;
        ForeColor = Theme.TextPrimary;
        Font = Theme.UiFont;
        MinimumSize = new Size(840, 580);
        Size = new Size(1000, 740);
        StartPosition = FormStartPosition.CenterScreen;
        try
        {
            Icon = SystemIcons.Application;
        }
        catch { /* no icon, fine */ }

        BuildSidebar();
        BuildContent();

        Controls.Add(_content);
        Controls.Add(_sidebar);
        BuildMenu(); // added last so the menu strip owns the top edge

        _controller.Changed += OnControllerChanged;

        Load += (_, _) =>
        {
            Theme.UseDarkTitleBar(Handle);
            Select(_nav[0].page);
        };
    }

    // ── layout ──────────────────────────────────────────────────────────

    private void BuildMenu()
    {
        var menu = new MenuStrip
        {
            BackColor = Theme.Base,
            ForeColor = Theme.TextMuted,
            Renderer = new ToolStripProfessionalRenderer(new DarkMenuColors()),
        };

        var file = new ToolStripMenuItem("File");
        file.DropDownItems.Add("Hide to tray", null, (_, _) => Hide());
        var exit = new ToolStripMenuItem("Exit", null, (_, _) => ExitRequested?.Invoke());
        file.DropDownItems.Add(exit);

        var presence = new ToolStripMenuItem("Presence");
        presence.DropDownItems.Add("Refresh now", null, async (_, _) => await _controller.ForceRefreshAsync());
        presence.DropDownItems.Add("Reload projects", null, async (_, _) => await _controller.RefreshProjectsAsync());
        presence.DropDownItems.Add(new ToolStripSeparator());
        presence.DropDownItems.Add("Show weekly recap", null, (_, _) => _controller.ShowWeeklyRecapNow());

        var help = new ToolStripMenuItem("Help");
        help.DropDownItems.Add("Open logs folder", null, (_, _) => OpenPath(AppPaths.LogDirectory));
        help.DropDownItems.Add("Open icons folder", null, (_, _) => OpenPath(_controller.IconsDirectory));
        help.DropDownItems.Add("Open custom Pixoo screens folder", null, (_, _) => OpenPath(_controller.Pixoo.CustomScreensDirectory));
        help.DropDownItems.Add(new ToolStripSeparator());
        help.DropDownItems.Add("About SmartCord", null, (_, _) => MessageBox.Show(
            "SmartCord — Discord Rich Presence for the DeltaVDevs workflow.\n\nRuns in the tray, drives your Discord status from what you're working on.",
            "About SmartCord", MessageBoxButtons.OK, MessageBoxIcon.Information));

        menu.Items.AddRange([file, presence, help]);
        MainMenuStrip = menu;
        Controls.Add(menu);
    }

    internal static void OpenPath(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = path, UseShellExecute = true });
        }
        catch { /* best effort */ }
    }

    private void BuildSidebar()
    {
        _sidebar.Dock = DockStyle.Left;
        _sidebar.Width = 208;
        _sidebar.BackColor = Theme.Base;
        _sidebar.Padding = new Padding(0, 0, 0, 12);

        var brand = new Label
        {
            Text = "  SmartCord",
            Dock = DockStyle.Top,
            Height = 60,
            Font = new Font("Segoe UI Semibold", 14f),
            ForeColor = Theme.TextPrimary,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(12, 0, 0, 0),
        };

        var pages = new SmartCordPage[]
        {
            new DashboardPage(_controller),
            new ProjectsPage(_controller),
            new PresetsPage(_controller),
            new IntegrationsPage(_controller),
            new PixooPage(_controller),
            new CustomPresencePage(_controller),
            new SettingsPage(_controller),
            new LogsPage(_controller),
        };

        // Buttons are added top-down, so add in reverse for Dock.Top ordering.
        var navHost = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Base };

        // Dock, not a manually-positioned Point over a Fill sibling -- that combination
        // (the old code) only lines up by coincidence at one specific DPI/font size and
        // is exactly the kind of layout that breaks first on an unusual display.
        var statusPanel = new Panel { Dock = DockStyle.Bottom, Height = 84, BackColor = Theme.Base, Padding = new Padding(14, 6, 10, 8) };
        _statusDot.Text = "●";
        _statusDot.AutoSize = false;
        _statusDot.Dock = DockStyle.Left;
        _statusDot.Width = 20;
        _statusDot.TextAlign = ContentAlignment.TopCenter;
        _statusDot.ForeColor = Theme.TextFaint;
        _statusText.AutoSize = false;
        _statusText.Dock = DockStyle.Fill;
        _statusText.Padding = new Padding(4, 0, 4, 0);
        _statusText.TextAlign = ContentAlignment.TopLeft;
        _statusText.Font = new Font("Segoe UI", 8.25f);
        _statusText.ForeColor = Theme.TextMuted;
        _statusText.Text = "Starting…";
        statusPanel.Controls.Add(_statusText);
        statusPanel.Controls.Add(_statusDot);

        foreach (var page in pages.Reverse())
        {
            var button = NavButton(page.Title);
            button.Click += (_, _) => Select(page);
            navHost.Controls.Add(button);
            button.Dock = DockStyle.Top;
            _nav.Insert(0, (button, page));
        }

        _sidebar.Controls.Add(navHost);
        _sidebar.Controls.Add(statusPanel);
        _sidebar.Controls.Add(brand);
    }

    private static Button NavButton(string text) => new()
    {
        Text = "   " + text,
        Height = 40,
        TextAlign = ContentAlignment.MiddleLeft,
        FlatStyle = FlatStyle.Flat,
        BackColor = Theme.Base,
        ForeColor = Theme.TextMuted,
        Font = Theme.UiFont,
        Cursor = Cursors.Hand,
        Margin = new Padding(0),
        Padding = new Padding(8, 0, 0, 0),
        FlatAppearance = { BorderSize = 0, MouseOverBackColor = Theme.Surface },
    };

    private void BuildContent()
    {
        _content.Dock = DockStyle.Fill;
        _content.BackColor = Theme.Surface;
    }

    private void Select(SmartCordPage page)
    {
        if (_activePage == page)
        {
            return;
        }

        _content.SuspendLayout();
        if (_activePage is not null)
        {
            _content.Controls.Remove(_activePage);
        }

        _activePage = page;
        if (!_content.Controls.Contains(page))
        {
            _content.Controls.Add(page);
        }
        page.Visible = true;
        page.BringToFront();
        _content.ResumeLayout();

        foreach (var (button, p) in _nav)
        {
            var selected = p == page;
            button.BackColor = selected ? Theme.Surface : Theme.Base;
            button.ForeColor = selected ? Theme.TextPrimary : Theme.TextMuted;
            button.Font = selected ? Theme.UiFontBold : Theme.UiFont;
        }

        page.ApplyState();
    }

    // ── state ───────────────────────────────────────────────────────────

    private void OnControllerChanged()
    {
        if (IsDisposed || Disposing)
        {
            return;
        }

        var (dot, text) = StatusLine();
        _statusDot.ForeColor = dot;
        _statusText.Text = text;

        _activePage?.ApplyState();
    }

    private (Color, string) StatusLine()
    {
        if (!_controller.IsConfigured)
        {
            return (Theme.Red, "No Client ID set");
        }
        if (!_controller.IsEnabled)
        {
            return (Theme.TextFaint, "Rich Presence off");
        }
        if (_controller.SessionLocked && _controller.Settings.Presence.ClearOnLock)
        {
            return (Theme.Yellow, "Paused — session locked");
        }
        return _controller.IsConnected
            ? (Theme.Green, _controller.LastStatus)
            : (Theme.Yellow, "Connecting to Discord…");
    }

    // ── close / tray ────────────────────────────────────────────────────

    public void ShowAndFocus()
    {
        Show();
        if (WindowState == FormWindowState.Minimized)
        {
            WindowState = FormWindowState.Normal;
        }
        Activate();
        BringToFront();
    }

    public void ForceClose()
    {
        _exiting = true;
        Close();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_exiting && !AllowExit && e.CloseReason == CloseReason.UserClosing && _controller.MinimizeToTray)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _controller.Changed -= OnControllerChanged;
        }
        base.Dispose(disposing);
    }
}

/// <summary>Dark colours for the menu strip's professional renderer.</summary>
internal sealed class DarkMenuColors : ProfessionalColorTable
{
    public override Color MenuItemSelected => Theme.Surface;
    public override Color MenuItemSelectedGradientBegin => Theme.Surface;
    public override Color MenuItemSelectedGradientEnd => Theme.Surface;
    public override Color MenuItemBorder => Theme.Border;
    public override Color MenuBorder => Theme.Border;
    public override Color MenuItemPressedGradientBegin => Theme.Base;
    public override Color MenuItemPressedGradientEnd => Theme.Base;
    public override Color ToolStripDropDownBackground => Theme.SurfaceAlt;
    public override Color ImageMarginGradientBegin => Theme.SurfaceAlt;
    public override Color ImageMarginGradientMiddle => Theme.SurfaceAlt;
    public override Color ImageMarginGradientEnd => Theme.SurfaceAlt;
    public override Color SeparatorDark => Theme.Border;
    public override Color SeparatorLight => Theme.Border;
}
