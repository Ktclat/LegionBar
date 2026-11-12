namespace LegionBar;

internal readonly record struct ScreenWindow(Rectangle Frame, Rectangle Client, bool Opaque = true);

internal static class FullscreenDetection
{
    public static bool IsScreenFullscreen(Screen screen)
    {
        var coverage = new ScreenCoverage(screen.Bounds, screen.WorkingArea);
        Native.EnumWindows((window, _) =>
        {
            Native.GetWindowThreadProcessId(window, out var processId);
            if (processId == Environment.ProcessId || !Native.IsWindowVisible(window) || Native.IsIconic(window)) return true;
            var name = new System.Text.StringBuilder(256); Native.GetClassName(window, name, 256);
            if (name.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return true;
            var extendedStyle = Native.GetWindowLongPtr(window, -20).ToInt64();
            if ((extendedStyle & 0x80) != 0) return true; // Tool windows and floating palettes.
            Native.DwmGetWindowAttribute(window, 14, out var cloaked, 4);
            if (cloaked != 0 || !Native.GetWindowRect(window, out var frame) || !frame.Rectangle.IntersectsWith(screen.Bounds)) return true;
            if (Native.DwmGetWindowRectAttribute(window, 9, out var visibleFrame, 16) == 0) frame = visibleFrame;
            if (!Native.GetClientRect(window, out var client)) return true;
            var origin = new Native.Point(0, 0);
            if (!Native.ClientToScreen(window, ref origin)) return true;
            return coverage.Visit(new(frame.Rectangle, new Rectangle(origin.X, origin.Y, client.Right - client.Left, client.Bottom - client.Top), (extendedStyle & 0x80000) == 0));
        }, IntPtr.Zero);
        return coverage.Fullscreen;
    }

    // EnumWindows supplies front-to-back Z order. Focus can be on another display.
    // A fullscreen window entirely behind ordinary windows must not hide the panel.
    internal static bool HasExposedFullscreen(IEnumerable<ScreenWindow> windows, Rectangle monitor, Rectangle workArea)
    {
        var coverage = new ScreenCoverage(monitor, workArea);
        foreach (var window in windows) if (!coverage.Visit(window)) break;
        return coverage.Fullscreen;
    }

    sealed class ScreenCoverage(Rectangle monitor, Rectangle workArea)
    {
        List<Rectangle> exposed = [workArea];
        public bool Fullscreen { get; private set; }
        public bool Visit(ScreenWindow window)
        {
            if (exposed.Count == 0) return false;
            var c = window.Client;
            var fullscreen = c.Left <= monitor.Left + 1 && c.Top <= monitor.Top + 1 && c.Right >= monitor.Right - 1 && c.Bottom >= monitor.Bottom - 1;
            if (fullscreen && exposed.Any(area => area.IntersectsWith(window.Frame))) { Fullscreen = true; return false; }
            if (window.Opaque) exposed = exposed.SelectMany(area => Subtract(area, window.Frame)).ToList();
            return exposed.Count != 0;
        }
    }

    static IEnumerable<Rectangle> Subtract(Rectangle area, Rectangle cover)
    {
        var overlap = Rectangle.Intersect(area, cover);
        if (overlap.IsEmpty) { yield return area; yield break; }
        if (area.Top < overlap.Top) yield return Rectangle.FromLTRB(area.Left, area.Top, area.Right, overlap.Top);
        if (overlap.Bottom < area.Bottom) yield return Rectangle.FromLTRB(area.Left, overlap.Bottom, area.Right, area.Bottom);
        if (area.Left < overlap.Left) yield return Rectangle.FromLTRB(area.Left, overlap.Top, overlap.Left, overlap.Bottom);
        if (overlap.Right < area.Right) yield return Rectangle.FromLTRB(overlap.Right, overlap.Top, area.Right, overlap.Bottom);
    }

    public static Dictionary<string, bool> VerifyCases()
    {
        var monitor = new Rectangle(0, 0, 1920, 1080);
        var work = new Rectangle(0, 0, 1920, 1020);
        var full = new ScreenWindow(monitor, monitor);
        var second = new Rectangle(1920, 0, 1920, 1080);
        return new()
        {
            ["VideoRemainsFullscreenWhenFocusMovesToSecondDisplay"] = HasExposedFullscreen([new(second, second), full], monitor, work),
            ["FullscreenOnSecondDisplayDoesNotHidePrimaryPanel"] = !HasExposedFullscreen([new(second, second)], monitor, work),
            ["OrdinaryMaximizedWindowIsNotFullscreen"] = !HasExposedFullscreen([new(work, new Rectangle(0, 30, 1920, 990))], monitor, work),
            ["CoveredFullscreenWindowDoesNotHidePanel"] = !HasExposedFullscreen([new(work, new Rectangle(0, 30, 1920, 990)), full], monitor, work),
            ["SmallPopupDoesNotEndFullscreen"] = HasExposedFullscreen([new(new Rectangle(20, 20, 400, 300), new Rectangle(20, 20, 400, 300)), full], monitor, work),
            ["TwoOrdinaryWindowsCanFullyCoverBackgroundVideo"] = !HasExposedFullscreen([new(new Rectangle(0, 0, 960, 1020), new Rectangle(0, 30, 960, 990)), new(new Rectangle(960, 0, 960, 1020), new Rectangle(960, 30, 960, 990)), full], monitor, work),
            ["NegativeMonitorCoordinatesAreSupported"] = HasExposedFullscreen([new(new Rectangle(-1920, 0, 1920, 1080), new Rectangle(-1920, 0, 1920, 1080))], new Rectangle(-1920, 0, 1920, 1080), new Rectangle(-1920, 0, 1920, 1020))
        };
    }
}
