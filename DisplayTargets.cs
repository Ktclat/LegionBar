namespace LegionBar;

internal static class DisplayTargets
{
    public static HashSet<string> Select(string mode, IEnumerable<string> selected, IReadOnlyList<string> connected, string primary)
    {
        var targets = mode == "all" ? connected.ToHashSet() : mode == "selected" ? selected.Intersect(connected).ToHashSet() : new HashSet<string> { primary };
        if (targets.Count == 0 && connected.Contains(primary)) targets.Add(primary);
        return targets;
    }
    public static string? TaskbarOwner(Rectangle bar, IReadOnlyList<(string Name, Rectangle Bounds)> displays)
    {
        // Prefer bottom edges globally: an auto-hidden upper taskbar can overlap
        // the top of the display below it without belonging to that display.
        foreach (var display in displays)
            if (Math.Abs(bar.Left - display.Bounds.Left) <= 4 && Math.Abs(bar.Width - display.Bounds.Width) <= 8 && bar.Top >= display.Bounds.Bottom - bar.Height - 4 && bar.Top <= display.Bounds.Bottom + 4) return display.Name;
        foreach (var display in displays)
            if (bar.IntersectsWith(display.Bounds)) return display.Name;
        return null;
    }
    public static Dictionary<string, bool> VerifyCases()
    {
        string[] screens = ["primary", "second", "third"];
        return new()
        {
            ["PrimaryOnly"] = Select("primary", [], screens, "primary").SetEquals(["primary"]),
            ["AllIncludesNewDisplays"] = Select("all", [], screens, "primary").SetEquals(screens),
            ["OneOrMoreSelectedDisplays"] = Select("selected", ["second", "third"], screens, "primary").SetEquals(["second", "third"]),
            ["DisconnectedSelectionFallsBackToPrimary"] = Select("selected", ["unplugged"], screens, "primary").SetEquals(["primary"]),
            ["ReconnectedSelectionReturns"] = Select("selected", ["second"], screens, "primary").SetEquals(["second"]),
            ["HiddenTaskbarStaysOnUpperDisplay"] = TaskbarOwner(new Rectangle(0, 1078, 1920, 60), [("primary", new Rectangle(0, 0, 1920, 1080)), ("second", new Rectangle(0, 1080, 1920, 1080))]) == "primary",
            ["SecondaryTaskbarIsDetected"] = TaskbarOwner(new Rectangle(0, 2100, 1920, 60), [("primary", new Rectangle(0, 0, 1920, 1080)), ("second", new Rectangle(0, 1080, 1920, 1080))]) == "second"
        };
    }
}

internal static class Taskbars
{
    public static IntPtr Find(Screen screen)
    {
        if (screen.Primary) return Native.FindWindow("Shell_TrayWnd", null);
        var found = IntPtr.Zero;
        var displays = Screen.AllScreens.Select(s => (s.DeviceName, s.Bounds)).ToArray();
        Native.EnumWindows((window, _) =>
        {
            var name = new System.Text.StringBuilder(256); Native.GetClassName(window, name, 256);
            if (name.ToString() != "Shell_SecondaryTrayWnd" || !Native.GetWindowRect(window, out var rect)) return true;
            if (DisplayTargets.TaskbarOwner(rect.Rectangle, displays) != screen.DeviceName) return true;
            found = window; return false;
        }, IntPtr.Zero);
        return found;
    }
}
