namespace LegionBar;

internal static class MenuPlacement
{
    public static Point AboveRight(Point anchor, Size size, Rectangle workingArea, int ceiling)
    {
        var bottom = Math.Clamp(Math.Min(anchor.Y, ceiling), workingArea.Top, workingArea.Bottom);
        return new Point(
            Math.Clamp(anchor.X, workingArea.Left, Math.Max(workingArea.Left, workingArea.Right - size.Width)),
            Math.Clamp(bottom - size.Height, workingArea.Top, Math.Max(workingArea.Top, workingArea.Bottom - size.Height)));
    }
}
