using System.Drawing.Drawing2D;

namespace SmartCord.Ui;

/// <summary>
/// A read-only mock of the Discord Rich Presence card SmartCord is currently
/// pushing: large image + small badge, the two text lines, an elapsed timer and
/// up to two buttons. Images are pulled from the workspace <c>icons/</c> folder by
/// asset key; anything missing falls back to a lettered tile.
/// </summary>
public sealed class PresenceCardControl : Control
{
    private readonly System.Windows.Forms.Timer _tick = new() { Interval = 1000 };
    private readonly Dictionary<string, Image?> _imageCache = new(StringComparer.OrdinalIgnoreCase);

    private string _iconsDirectory = "";
    private PresencePayload? _payload;
    private DateTime _startedAtUtc = DateTime.UtcNow;
    private bool _enabled = true;
    private string _placeholderTitle = "Nothing playing";

    public PresenceCardControl()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
        BackColor = Theme.Card;
        Height = 150;
        _tick.Tick += (_, _) => Invalidate();
        _tick.Start();
    }

    public string IconsDirectory
    {
        set
        {
            if (_iconsDirectory != value)
            {
                _iconsDirectory = value;
                _imageCache.Clear();
                Invalidate();
            }
        }
    }

    public void Show(PresencePayload? payload, DateTime startedAtUtc, bool presenceEnabled, string placeholderTitle)
    {
        _payload = payload;
        _startedAtUtc = startedAtUtc;
        _enabled = presenceEnabled;
        _placeholderTitle = placeholderTitle;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        using (var bg = new SolidBrush(Theme.Card))
        using (var pen = new Pen(Theme.Border))
        {
            var outline = new Rectangle(0, 0, Width - 1, Height - 1);
            using var path = Rounded(outline, 10);
            g.FillPath(bg, path);
            g.DrawPath(pen, path);
        }

        const int pad = 16;
        var art = new Rectangle(pad, pad, 60, 60);
        DrawArt(g, art, _payload?.LargeImageKey, _payload?.SmallImageKey);

        var textLeft = art.Right + 14;
        var textWidth = Width - textLeft - pad;

        if (_payload is null)
        {
            using var faint = new SolidBrush(Theme.TextFaint);
            g.DrawString(_placeholderTitle, Theme.UiFontBold, faint,
                new RectangleF(textLeft, pad + 4, textWidth, 20));
            return;
        }

        using var primary = new SolidBrush(_enabled ? Theme.TextPrimary : Theme.TextFaint);
        using var muted = new SolidBrush(Theme.TextMuted);
        using var faintBrush = new SolidBrush(Theme.TextFaint);

        var format = new StringFormat(StringFormatFlags.NoWrap) { Trimming = StringTrimming.EllipsisCharacter, LineAlignment = StringAlignment.Center };
        var lineH = 21f;
        var y = (float)pad;

        g.DrawString(NonEmpty(_payload.Details, "Coding"), Theme.UiFontBold, primary,
            new RectangleF(textLeft, y, textWidth, lineH), format);
        y += lineH;
        g.DrawString(NonEmpty(_payload.State, "—"), Theme.UiFont, muted,
            new RectangleF(textLeft, y, textWidth, lineH), format);
        y += lineH;

        var elapsed = DateTime.UtcNow - _startedAtUtc;
        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }
        var elapsedText = elapsed.TotalHours >= 1
            ? $"{(int)elapsed.TotalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00} elapsed"
            : $"{elapsed.Minutes:00}:{elapsed.Seconds:00} elapsed";
        g.DrawString(elapsedText, Theme.UiFont, faintBrush,
            new RectangleF(textLeft, y, textWidth, lineH), format);

        // buttons
        var buttons = new List<(string label, string url)>();
        AddButton(buttons, _payload.PrimaryButtonLabel, _payload.PrimaryButtonUrl);
        AddButton(buttons, _payload.SecondaryButtonLabel, _payload.SecondaryButtonUrl);

        var by = pad + (int)(lineH * 3) + 6;
        var bx = textLeft;
        foreach (var (label, _) in buttons)
        {
            var size = g.MeasureString(label, Theme.UiFont);
            var rect = new Rectangle(bx, by, (int)size.Width + 20, 24);
            if (rect.Right > Width - pad)
            {
                break;
            }
            using var fill = new SolidBrush(Theme.SurfaceAlt);
            using var bpen = new Pen(Theme.Border);
            using var path = Rounded(rect, 6);
            g.FillPath(fill, path);
            g.DrawPath(bpen, path);
            g.DrawString(label, Theme.UiFont, muted, rect.X + 10, rect.Y + 4);
            bx = rect.Right + 8;
        }

        if (!_enabled)
        {
            using var overlay = new SolidBrush(Color.FromArgb(150, Theme.Card));
            g.FillRectangle(overlay, ClientRectangle);
            using var f = new SolidBrush(Theme.TextMuted);
            var msg = "Rich Presence disabled";
            var s = g.MeasureString(msg, Theme.UiFontBold);
            g.DrawString(msg, Theme.UiFontBold, f, (Width - s.Width) / 2, (Height - s.Height) / 2);
        }
    }

    private void DrawArt(Graphics g, Rectangle art, string? largeKey, string? smallKey)
    {
        var large = Resolve(largeKey);
        using (var path = Rounded(art, 10))
        {
            if (large is not null)
            {
                var clip = g.Clip;
                g.SetClip(path);
                g.DrawImage(large, art);
                g.Clip = clip;
            }
            else
            {
                using var fill = new SolidBrush(Theme.Accent);
                g.FillPath(fill, path);
                var letter = string.IsNullOrWhiteSpace(largeKey) ? "?" : largeKey!.Trim()[..1].ToUpperInvariant();
                using var f = new SolidBrush(Color.White);
                using var font = new Font("Segoe UI Semibold", 22f);
                var s = g.MeasureString(letter, font);
                g.DrawString(letter, font, f, art.X + (art.Width - s.Width) / 2, art.Y + (art.Height - s.Height) / 2);
            }
            using var pen = new Pen(Theme.Border);
            g.DrawPath(pen, path);
        }

        var small = Resolve(smallKey);
        if (small is not null || !string.IsNullOrWhiteSpace(smallKey))
        {
            var badge = new Rectangle(art.Right - 20, art.Bottom - 20, 22, 22);
            using var ring = new SolidBrush(Theme.Card);
            g.FillEllipse(ring, badge.X - 2, badge.Y - 2, badge.Width + 4, badge.Height + 4);
            if (small is not null)
            {
                var clip = g.Clip;
                using var ePath = new GraphicsPath();
                ePath.AddEllipse(badge);
                g.SetClip(ePath);
                g.DrawImage(small, badge);
                g.Clip = clip;
            }
            else
            {
                using var fill = new SolidBrush(Theme.SurfaceAlt);
                g.FillEllipse(fill, badge);
            }
        }
    }

    private Image? Resolve(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        if (_imageCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        Image? image = null;
        try
        {
            foreach (var candidate in new[]
                     {
                         Path.Combine(_iconsDirectory, key + ".png"),
                         Path.Combine(_iconsDirectory, key + ".jpg"),
                         Path.Combine(_iconsDirectory, "projects", key + ".png"),
                         Path.Combine(_iconsDirectory, "projects", key + ".jpg"),
                     })
            {
                if (File.Exists(candidate))
                {
                    using var fs = new FileStream(candidate, FileMode.Open, FileAccess.Read);
                    image = Image.FromStream(fs);
                    break;
                }
            }
        }
        catch
        {
            image = null;
        }

        _imageCache[key] = image;
        return image;
    }

    private static void AddButton(List<(string, string)> list, string? label, string? url)
    {
        if (list.Count >= 2 || string.IsNullOrWhiteSpace(label) || string.IsNullOrWhiteSpace(url))
        {
            return;
        }
        if (Uri.TryCreate(url, UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps))
        {
            list.Add((label!.Trim(), url!.Trim()));
        }
    }

    private static string NonEmpty(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value!;

    private static GraphicsPath Rounded(Rectangle r, int radius)
    {
        var d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _tick.Dispose();
            foreach (var image in _imageCache.Values)
            {
                image?.Dispose();
            }
        }
        base.Dispose(disposing);
    }
}
