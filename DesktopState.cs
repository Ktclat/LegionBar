using Microsoft.Win32;
namespace LegionBar;

// One process-wide desktop snapshot, shared by every display panel. WinEvent
// callbacks only invalidate state; all inspection stays on the UI thread.
internal sealed class DesktopState : IDisposable
{
    readonly Native.WinEventCallback callback;
    readonly List<IntPtr> hooks = new();
    readonly Dictionary<string, IntPtr> taskbars = new();
    readonly Dictionary<string, bool> fullscreen = new();
    int dirty = 1, themeDirty = 1;
    long nextFallback, nextThemeRead;
    public Screen[] Screens { get; private set; } = Screen.AllScreens;
    public int TopologyVersion { get; private set; }
    public int SceneVersion { get; private set; }
    public int MenuVersion { get; private set; }
    public bool SystemDark { get; private set; } = true;
    public DesktopState()
    {
        callback = (_, _, _, objectId, childId, _, _) => { if (objectId == 0 && childId == 0) Volatile.Write(ref dirty, 1); };
        foreach (var (first, last) in new[] { (3u, 23u), (0x8000u, 0x800Bu) })
        {
            var hook = Native.SetWinEventHook(first, last, IntPtr.Zero, callback, 0, 0, 2);
            if (hook != IntPtr.Zero) hooks.Add(hook);
        }
        SystemEvents.DisplaySettingsChanged += DisplaysChanged;
        SystemEvents.UserPreferenceChanged += PreferenceChanged;
        Refresh();
    }
    void DisplaysChanged(object? sender, EventArgs e) => Volatile.Write(ref dirty, 1);
    void PreferenceChanged(object sender, UserPreferenceChangedEventArgs e) => Volatile.Write(ref themeDirty, 1);
    public void Invalidate() => Volatile.Write(ref dirty, 1);
    public IntPtr Taskbar(Screen screen) => taskbars.GetValueOrDefault(screen.DeviceName);
    public bool IsFullscreen(Screen screen) => fullscreen.GetValueOrDefault(screen.DeviceName);
    public void Refresh()
    {
        var now = Environment.TickCount64;
        if (Interlocked.Exchange(ref themeDirty, 0) != 0 || now >= nextThemeRead)
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            SystemDark = Convert.ToInt32(key?.GetValue("SystemUsesLightTheme", 0)) == 0;
            nextThemeRead = now + 30000;
            PerformanceProbe.Count("Theme.RegistryRead");
        }
        if (Interlocked.Exchange(ref dirty, 0) == 0 && now < nextFallback) return;
        using var measurement = PerformanceProbe.Measure("Desktop.Capture");
        nextFallback = now + (hooks.Count == 0 ? 240 : 1500);
        var nextScreens = Screen.AllScreens;
        if (!Screens.Select(s => (s.DeviceName, s.Bounds, s.WorkingArea, s.Primary)).SequenceEqual(nextScreens.Select(s => (s.DeviceName, s.Bounds, s.WorkingArea, s.Primary)))) { TopologyVersion++; MenuVersion++; }
        Screens = nextScreens;
        var previousTaskbars = taskbars.ToArray();
        taskbars.Clear();
        var displays = Screens.Select(s => (s.DeviceName, s.Bounds)).ToArray();
        var windows = FullscreenDetection.Capture((window, name, frame) =>
        {
            if (name == "Shell_TrayWnd") { var primary = Screens.FirstOrDefault(s => s.Primary); if (primary != null) taskbars[primary.DeviceName] = window; }
            else if (name == "Shell_SecondaryTrayWnd" && DisplayTargets.TaskbarOwner(frame, displays) is { } owner) taskbars[owner] = window;
        });
        fullscreen.Clear();
        foreach (var screen in Screens) fullscreen[screen.DeviceName] = FullscreenDetection.HasExposedFullscreen(windows, screen.Bounds, screen.WorkingArea);
        if (previousTaskbars.Length != taskbars.Count || previousTaskbars.Any(pair => taskbars.GetValueOrDefault(pair.Key) != pair.Value)) MenuVersion++;
        SceneVersion++;
    }
    public void Dispose()
    {
        SystemEvents.DisplaySettingsChanged -= DisplaysChanged;
        SystemEvents.UserPreferenceChanged -= PreferenceChanged;
        foreach (var hook in hooks) Native.UnhookWinEvent(hook);
    }
}
