using System.Runtime.InteropServices;

namespace SmartCord.Ui;

/// <summary>
/// A ComboBox that actually looks dark: owner-drawn items and closed box, plus a
/// dark window theme pushed onto the drop-down list so its background and
/// scrollbar match. Defaults to <see cref="ComboBoxStyle.DropDownList"/>.
/// </summary>
public sealed class DarkComboBox : ComboBox
{
    public DarkComboBox()
    {
        DropDownStyle = ComboBoxStyle.DropDownList;
        FlatStyle = FlatStyle.Flat;
        DrawMode = DrawMode.OwnerDrawFixed;
        BackColor = Theme.SurfaceAlt;
        ForeColor = Theme.TextPrimary;
        ItemHeight = 22;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        DarkMode.ApplyToControl(Handle, "DarkMode_CFD");
    }

    protected override void OnDropDown(EventArgs e)
    {
        base.OnDropDown(e);
        if (DarkMode.TryGetComboListHandle(Handle, out var list))
        {
            DarkMode.ApplyToControl(list, "DarkMode_Explorer");
        }
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        var selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
        var back = selected ? Theme.Accent : Theme.SurfaceAlt;
        var fore = selected ? Color.White : (Enabled ? Theme.TextPrimary : Theme.TextFaint);

        using (var b = new SolidBrush(back))
        {
            e.Graphics.FillRectangle(b, e.Bounds);
        }

        var text = e.Index >= 0 ? GetItemText(Items[e.Index]) : Text;
        TextRenderer.DrawText(e.Graphics, text, Font, e.Bounds, fore,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        // Draw the drop arrow ourselves — the flat combo's own glyph is a grey box.
        var g = e.Graphics;
        using var border = new Pen(Theme.Border);
        g.DrawRectangle(border, 0, 0, Width - 1, Height - 1);

        var mid = Height / 2;
        var x = Width - 16;
        using var arrow = new SolidBrush(Enabled ? Theme.TextMuted : Theme.TextFaint);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.FillPolygon(arrow, new[]
        {
            new Point(x, mid - 2),
            new Point(x + 8, mid - 2),
            new Point(x + 4, mid + 3),
        });
    }
}

/// <summary>NumericUpDown with dark chrome — the up/down buttons stay native but
/// the field and border match the rest of the form.</summary>
public sealed class DarkNumericUpDown : NumericUpDown
{
    public DarkNumericUpDown()
    {
        BorderStyle = BorderStyle.FixedSingle;
        BackColor = Theme.SurfaceAlt;
        ForeColor = Theme.TextPrimary;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        DarkMode.ApplyToControl(Handle, "DarkMode_CFD");
        foreach (Control child in Controls)
        {
            DarkMode.ApplyToControl(child.Handle, "DarkMode_Explorer");
        }
    }
}

/// <summary>Read-only multiline text box styled as a dark panel (log / detail views).</summary>
public sealed class DarkTextBox : TextBox
{
    public DarkTextBox()
    {
        BackColor = Theme.SurfaceAlt;
        ForeColor = Theme.TextPrimary;
        BorderStyle = BorderStyle.FixedSingle;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        DarkMode.ApplyToControl(Handle, "DarkMode_Explorer");
    }
}
