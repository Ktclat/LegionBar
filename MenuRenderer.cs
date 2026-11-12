using System.Drawing.Drawing2D;
namespace LegionBar;

internal sealed class MenuRenderer(bool dark) : ToolStripRenderer
{
    public Color Background => dark ? Color.FromArgb(35, 36, 40) : Color.FromArgb(250, 250, 252);
    public Color Foreground => dark ? Color.FromArgb(239, 240, 244) : Color.FromArgb(30, 32, 38);
    Color Muted => dark ? Color.FromArgb(147, 151, 162) : Color.FromArgb(118, 123, 135);
    Color Border => dark ? Color.FromArgb(65, 67, 75) : Color.FromArgb(213, 216, 224);
    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e) { using var b = new SolidBrush(Background); e.Graphics.FillRectangle(b, e.AffectedBounds); }
    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { using var b = new SolidBrush(Background); e.Graphics.FillRectangle(b, e.AffectedBounds); }
    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e) { using var p = new Pen(Border); e.Graphics.DrawRectangle(p, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1); }
    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if (!e.Item.Enabled || (!e.Item.Selected && !e.Item.Pressed)) return;
        using var b = new SolidBrush(dark ? Color.FromArgb(57, 60, 68) : Color.FromArgb(231, 234, 240));
        e.Graphics.FillRectangle(b, new Rectangle(4, 1, e.Item.Width - 8, e.Item.Height - 2));
    }
    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e) { e.TextColor = e.Item.Enabled ? Foreground : Muted; base.OnRenderItemText(e); }
    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e) { e.ArrowColor = e.Item?.Enabled == true ? Foreground : Muted; base.OnRenderArrow(e); }
    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        var r = e.ImageRectangle; var x = r.Left + r.Width / 2f; var y = r.Top + r.Height / 2f;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var p = new Pen(dark ? Color.FromArgb(126, 213, 200) : Color.FromArgb(17, 115, 103), 1.8f);
        e.Graphics.DrawLines(p, new PointF[] { new(x - 4, y), new(x - 1, y + 3), new(x + 5, y - 4) });
    }
    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e) { using var p = new Pen(Border); e.Graphics.DrawLine(p, 12, e.Item.Height / 2, e.Item.Width - 12, e.Item.Height / 2); }
}
