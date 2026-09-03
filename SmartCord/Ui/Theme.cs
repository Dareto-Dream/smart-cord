using System.Runtime.InteropServices;

namespace SmartCord.Ui;

/// <summary>
/// SmartCord's dark palette + a handful of helpers to drag WinForms controls into
/// looking like they belong in the same decade as Discord. Nothing fancy — flat
/// colours, no custom rendering beyond what a hobby tray app needs.
/// </summary>
public static class Theme
{
    public static readonly Color Base = Color.FromArgb(30, 31, 34);        // window / sidebar
    public static readonly Color Surface = Color.FromArgb(43, 45, 49);     // content area
    public static readonly Color SurfaceAlt = Color.FromArgb(49, 51, 56);  // inputs, rows
    public static readonly Color Card = Color.FromArgb(35, 36, 40);        // preview card
    public static readonly Color Border = Color.FromArgb(60, 62, 68);

    public static readonly Color TextPrimary = Color.FromArgb(242, 243, 245);
    public static readonly Color TextMuted = Color.FromArgb(181, 186, 193);
    public static readonly Color TextFaint = Color.FromArgb(128, 132, 142);

    public static readonly Color Accent = Color.FromArgb(88, 101, 242);    // blurple
    public static readonly Color AccentHover = Color.FromArgb(105, 116, 245);
    public static readonly Color Green = Color.FromArgb(35, 165, 90);
    public static readonly Color Red = Color.FromArgb(242, 63, 67);
    public static readonly Color Yellow = Color.FromArgb(240, 178, 50);

    public static readonly Font UiFont = new("Segoe UI", 9.75f);
    public static readonly Font UiFontBold = new("Segoe UI", 9.75f, FontStyle.Bold);
    public static readonly Font HeadingFont = new("Segoe UI Semibold", 15f);
    public static readonly Font SubheadingFont = new("Segoe UI Semibold", 10.5f);

    /// <summary>Tell the DWM to paint this window's title bar dark.</summary>
    public static void UseDarkTitleBar(IntPtr handle)
    {
        try
        {
            int on = 1;
            // 20 = DWMWA_USE_IMMERSIVE_DARK_MODE on Windows 10 20H1+ / 11
            DwmSetWindowAttribute(handle, 20, ref on, sizeof(int));
        }
        catch
        {
            // Older Windows — no dark title bar, no big deal.
        }
    }

    /// <summary>Style a primary (accent) button.</summary>
    public static void StylePrimary(Button button)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.BackColor = Accent;
        button.ForeColor = Color.White;
        button.Font = UiFontBold;
        button.Cursor = Cursors.Hand;
        SizeButton(button);
        button.FlatAppearance.MouseOverBackColor = AccentHover;
        button.FlatAppearance.MouseDownBackColor = Accent;
    }

    /// <summary>Style a secondary (outline) button.</summary>
    public static void StyleSecondary(Button button)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderColor = Border;
        button.FlatAppearance.BorderSize = 1;
        button.BackColor = SurfaceAlt;
        button.ForeColor = TextPrimary;
        button.Font = UiFont;
        button.Cursor = Cursors.Hand;
        SizeButton(button);
        button.FlatAppearance.MouseOverBackColor = Border;
    }

    private static void SizeButton(Button button)
    {
        button.AutoSize = true;
        button.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        button.MinimumSize = new Size(0, 32);
        button.Padding = new Padding(16, 6, 16, 6);
        button.Margin = new Padding(0, 0, 8, 0);
    }

    public static void StyleInput(Control input)
    {
        input.BackColor = SurfaceAlt;
        input.ForeColor = TextPrimary;
        input.Font = UiFont;
        if (input is TextBox tb)
        {
            tb.BorderStyle = BorderStyle.FixedSingle;
        }
        if (input is ComboBox cb)
        {
            cb.FlatStyle = FlatStyle.Flat;
        }
    }

    public static Label Heading(string text) => new()
    {
        Text = text,
        Font = HeadingFont,
        ForeColor = TextPrimary,
        AutoSize = true,
        Margin = new Padding(0, 0, 0, 12)
    };

    public static Label Subheading(string text) => new()
    {
        Text = text,
        Font = SubheadingFont,
        ForeColor = TextMuted,
        AutoSize = true,
        Margin = new Padding(0, 10, 0, 6)
    };

    public static Label Caption(string text) => new()
    {
        Text = text,
        Font = UiFont,
        ForeColor = TextFaint,
        AutoSize = true
    };

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
}
