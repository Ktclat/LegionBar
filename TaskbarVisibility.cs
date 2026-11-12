namespace LegionBar;
internal static class TaskbarVisibility
{
    public static bool ShouldShow(bool visible, bool cloaked, Rectangle taskbar, Rectangle screen, bool fullscreen)
    {
        var exposed = Rectangle.Intersect(taskbar, screen);
        return visible && !cloaked && !fullscreen && taskbar.Height >= 12 && exposed.Height >= Math.Max(8, taskbar.Height / 2);
    }
    public static bool VerifyCases()
    {
        var screen = new Rectangle(0, 0, 1920, 1080);
        var shown = new Rectangle(0, 1020, 1920, 60);
        return ShouldShow(true, false, shown, screen, false)
            && !ShouldShow(false, false, shown, screen, false)
            && !ShouldShow(true, true, shown, screen, false)
            && !ShouldShow(true, false, shown, screen, true)
            && !ShouldShow(true, false, new Rectangle(0, 1078, 1920, 60), screen, false)
            && ShouldShow(true, false, new Rectangle(0, 2100, 1920, 60), new Rectangle(0, 1080, 1920, 1080), false);
    }
}
