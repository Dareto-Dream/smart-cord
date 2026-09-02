namespace SmartCord.Ui;

/// <summary>Base for the main window's content pages. Each gets the controller and
/// a single <see cref="ApplyState"/> hook the shell calls when data changes.</summary>
public abstract class SmartCordPage : UserControl
{
    protected SmartCordController Controller { get; }

    protected SmartCordPage(SmartCordController controller)
    {
        Controller = controller;
        Dock = DockStyle.Fill;
        BackColor = Theme.Surface;
        ForeColor = Theme.TextPrimary;
        Font = Theme.UiFont;
        Padding = new Padding(28, 20, 28, 18);
        AutoScroll = true;
    }

    /// <summary>Human-readable name for the sidebar.</summary>
    public abstract string Title { get; }

    /// <summary>Re-read controller state into the controls. Called on show and on change.</summary>
    public abstract void ApplyState();

    /// <summary>A vertical stack that fills the page and scrolls.</summary>
    protected static FlowLayoutPanel Stack() => new()
    {
        Dock = DockStyle.Fill,
        FlowDirection = FlowDirection.TopDown,
        WrapContents = false,
        AutoScroll = true,
        BackColor = Theme.Surface,
    };
}
