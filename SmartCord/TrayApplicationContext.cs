using Microsoft.Extensions.Logging;
using SmartCord.Ui;

namespace SmartCord;

/// <summary>
/// Thin shell around <see cref="SmartCordController"/>: owns the tray icon and the
/// main window, routes controller notifications to balloon tips, and handles the
/// app lifecycle (show window / hide to tray / real exit).
/// </summary>
public sealed class TrayApplicationContext : ApplicationContext
{
    private readonly SmartCordController _controller;
    private readonly NotifyIcon _notifyIcon;
    private readonly MainForm _mainForm;
    private readonly ToolStripMenuItem _enabledItem;

    public TrayApplicationContext(SettingsProvider settingsProvider, ILoggerFactory loggerFactory, bool startHidden)
    {
        _controller = new SmartCordController(settingsProvider, loggerFactory);
        _controller.Notification += OnNotification;
        _controller.Changed += OnControllerChanged;

        _enabledItem = new ToolStripMenuItem("Rich Presence Enabled")
        {
            Checked = _controller.IsEnabled,
            CheckOnClick = true,
        };
        _enabledItem.CheckedChanged += (_, _) => _controller.SetEnabled(_enabledItem.Checked);

        var menu = new ContextMenuStrip();
        menu.Items.Add("Open SmartCord", null, (_, _) => ShowWindow());
        menu.Items.Add(_enabledItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitApp());

        _notifyIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "SmartCord",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _notifyIcon.DoubleClick += (_, _) => ShowWindow();

        _mainForm = new MainForm(_controller);
        _mainForm.ExitRequested += ExitApp;
        _mainForm.FormClosed += (_, _) => { /* hidden, not exited — handled in MainForm */ };

        _controller.Start();

        if (!startHidden)
        {
            ShowWindow();
        }
    }

    private void ShowWindow()
    {
        if (_mainForm.IsDisposed)
        {
            return;
        }
        _mainForm.ShowAndFocus();
    }

    private void OnControllerChanged()
    {
        if (_enabledItem.Checked != _controller.IsEnabled)
        {
            _enabledItem.Checked = _controller.IsEnabled;
        }
        _notifyIcon.Text = Trim($"SmartCord — {_controller.LastStatus}");
    }

    private void OnNotification(string title, string message, bool isError)
    {
        _notifyIcon.ShowBalloonTip(
            isError ? 5000 : 4000,
            title,
            message,
            isError ? ToolTipIcon.Warning : ToolTipIcon.Info);
    }

    private static string Trim(string text) => text.Length <= 63 ? text : text[..63];

    private void ExitApp()
    {
        _mainForm.AllowExit = true;
        _mainForm.ForceClose();
        ExitThread();
    }

    protected override void ExitThreadCore()
    {
        _controller.Changed -= OnControllerChanged;
        _controller.Notification -= OnNotification;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        if (!_mainForm.IsDisposed)
        {
            _mainForm.Dispose();
        }
        _controller.Dispose();
        base.ExitThreadCore();
    }
}
