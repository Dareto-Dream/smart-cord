using System.Drawing.Imaging;

namespace SmartCord.Pixoo;

/// <summary>
/// A 64×64 RGB framebuffer with the pixel-drawing helpers ported from
/// adamkdean/pixoo-api (GPL-3.0). Renders to the raw byte layout the Pixoo
/// expects (row-major, R,G,B per pixel) and to a scaled <see cref="Bitmap"/> for
/// the on-screen preview.
/// </summary>
public sealed class PixooCanvas
{
    public const int Size = 64;

    private readonly byte[] _rgb = new byte[Size * Size * 3];

    public void Clear() => Array.Clear(_rgb);

    public void Fill(Color c)
    {
        for (var i = 0; i < _rgb.Length; i += 3)
        {
            _rgb[i] = c.R;
            _rgb[i + 1] = c.G;
            _rgb[i + 2] = c.B;
        }
    }

    public void SetPixel(int x, int y, Color c)
    {
        if ((uint)x >= Size || (uint)y >= Size)
        {
            return;
        }
        var o = (y * Size + x) * 3;
        _rgb[o] = c.R;
        _rgb[o + 1] = c.G;
        _rgb[o + 2] = c.B;
    }

    public void HLine(int x, int y, int w, Color c)
    {
        for (var i = 0; i < w; i++)
        {
            SetPixel(x + i, y, c);
        }
    }

    public void VLine(int x, int y, int h, Color c)
    {
        for (var i = 0; i < h; i++)
        {
            SetPixel(x, y + i, c);
        }
    }

    public void Rect(int x, int y, int w, int h, Color c, bool fill)
    {
        if (fill)
        {
            for (var yy = 0; yy < h; yy++)
            {
                HLine(x, y + yy, w, c);
            }
            return;
        }
        HLine(x, y, w, c);
        HLine(x, y + h - 1, w, c);
        VLine(x, y, h, c);
        VLine(x + w - 1, y, h, c);
    }

    // ── text ───────────────────────────────────────────────────────────

    /// <summary>Rendered width of <paramref name="text"/> including the trailing 1px gap.</summary>
    public static int TextWidth(string text, int scale = 1)
    {
        var w = 0;
        foreach (var ch in text)
        {
            w += PixooFont.Width(ch) + 1;
        }
        return w * scale;
    }

    public void DrawChar(int x, int y, char ch, Color color, int scale = 1)
    {
        var glyph = PixooFont.Glyph(ch);
        var width = glyph.Length / PixooFont.Height;
        for (var i = 0; i < glyph.Length; i++)
        {
            if (glyph[i] == 0)
            {
                continue;
            }
            var gx = i % width;
            var gy = i / width;
            if (scale == 1)
            {
                SetPixel(x + gx, y + gy, color);
            }
            else
            {
                Rect(x + gx * scale, y + gy * scale, scale, scale, color, fill: true);
            }
        }
    }

    /// <summary>Draws left-aligned text; returns the x just past the last glyph.</summary>
    public int DrawText(int x, int y, string text, Color color, int scale = 1)
    {
        var cursor = x;
        foreach (var ch in text)
        {
            DrawChar(cursor, y, ch, color, scale);
            cursor += (PixooFont.Width(ch) + 1) * scale;
        }
        return cursor;
    }

    public void DrawTextCenter(int y, string text, Color color, int scale = 1)
    {
        var x = (Size - TextWidth(text, scale)) / 2;
        DrawText(x, y, text, color, scale);
    }

    /// <summary>Right edge of the text sits at <paramref name="rightX"/>.</summary>
    public void DrawTextRight(int rightX, int y, string text, Color color, int scale = 1)
    {
        DrawText(rightX - TextWidth(text, scale), y, text, color, scale);
    }

    /// <summary>Truncates <paramref name="text"/> to fit <paramref name="maxWidth"/> pixels, then draws it.</summary>
    public void DrawTextClipped(int x, int y, int maxWidth, string text, Color color, int scale = 1)
    {
        var fitted = Fit(text, maxWidth, scale);
        DrawText(x, y, fitted, color, scale);
    }

    /// <summary>Greedy word-wrap; draws up to <paramref name="maxLines"/> lines (last clipped). Returns lines drawn.</summary>
    public int DrawTextWrapped(int x, int y, int maxWidth, int lineHeight, int maxLines, string text, Color color, int scale = 1)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var line = "";
        var drawn = 0;
        foreach (var word in words)
        {
            var candidate = line.Length == 0 ? word : line + " " + word;
            if (TextWidth(candidate, scale) <= maxWidth)
            {
                line = candidate;
                continue;
            }
            if (line.Length > 0)
            {
                DrawText(x, y + drawn * lineHeight, line, color, scale);
                drawn++;
                line = word;
            }
            else
            {
                DrawText(x, y + drawn * lineHeight, Fit(word, maxWidth, scale), color, scale);
                drawn++;
                line = "";
            }
            if (drawn >= maxLines)
            {
                return drawn;
            }
        }
        if (line.Length > 0 && drawn < maxLines)
        {
            DrawTextClipped(x, y + drawn * lineHeight, maxWidth, line, color, scale);
            drawn++;
        }
        return drawn;
    }

    public static string Fit(string text, int maxWidth, int scale = 1)
    {
        if (TextWidth(text, scale) <= maxWidth)
        {
            return text;
        }
        for (var len = text.Length - 1; len > 0; len--)
        {
            var candidate = text[..len];
            if (TextWidth(candidate, scale) <= maxWidth)
            {
                return candidate;
            }
        }
        return "";
    }

    // ── widgets ────────────────────────────────────────────────────────

    public void ProgressBar(int x, int y, int w, int h, double fraction, Color fill, Color track, Color? border = null)
    {
        fraction = Math.Clamp(fraction, 0, 1);
        if (border is { } b)
        {
            Rect(x, y, w, h, b, fill: false);
            x += 1; y += 1; w -= 2; h -= 2;
        }
        Rect(x, y, w, h, track, fill: true);
        var filled = (int)Math.Round(w * fraction);
        if (filled > 0)
        {
            Rect(x, y, filled, h, fill, fill: true);
        }
    }

    // ── output ─────────────────────────────────────────────────────────

    public string ToBase64() => Convert.ToBase64String(_rgb);

    public byte[] Snapshot() => (byte[])_rgb.Clone();

    public long Fingerprint()
    {
        unchecked
        {
            long hash = 1469598103934665603;
            foreach (var b in _rgb)
            {
                hash = (hash ^ b) * 1099511628211;
            }
            return hash;
        }
    }

    public Bitmap ToBitmap(int scale)
    {
        var bmp = new Bitmap(Size * scale, Size * scale, PixelFormat.Format24bppRgb);
        var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
        try
        {
            var stride = data.Stride;
            var row = new byte[stride];
            for (var y = 0; y < Size; y++)
            {
                Array.Clear(row);
                for (var x = 0; x < Size; x++)
                {
                    var s = (y * Size + x) * 3;
                    // GDI 24bpp is BGR
                    var bcol = _rgb[s + 2];
                    var gcol = _rgb[s + 1];
                    var rcol = _rgb[s];
                    for (var sx = 0; sx < scale; sx++)
                    {
                        var d = (x * scale + sx) * 3;
                        row[d] = bcol;
                        row[d + 1] = gcol;
                        row[d + 2] = rcol;
                    }
                }
                for (var sy = 0; sy < scale; sy++)
                {
                    System.Runtime.InteropServices.Marshal.Copy(row, 0, data.Scan0 + (y * scale + sy) * stride, stride);
                }
            }
        }
        finally
        {
            bmp.UnlockBits(data);
        }
        return bmp;
    }

    public static byte[] SnapshotToBytes(PixooCanvas canvas) => canvas.Snapshot();
}
