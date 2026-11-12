using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Drawing.Imaging;
using System.Drawing.Text;
namespace LegionBar;
internal sealed class MonitorForm : Form
{
    readonly Settings settings;
    readonly MonitorForm? host;
    readonly string? displayDevice;
    readonly bool verifyDisplays;
    readonly bool benchmark;
    readonly DesktopState desktop;
    readonly NetworkInventory inventory;
    record PendingSample(Sample Sample, string Status);
    PendingSample? pendingSample;
    bool menuDirty = true;
    int menuGeneration;
    int menuNetworkVersion = -1, menuDesktopVersion = -1, knownTopology = -1, lastScenePositioned = -1;
    ToolStripMenuItem? startupItem;
    Font? menuFont;
    string lastVisualState = "";
    long benchmarkStart;
    TimeSpan benchmarkCpu;
    readonly Dictionary<string, MonitorForm> displayPanels = new();
    HashSet<string> selectedScreens = new();
    Screen? TargetScreen => host == null ? desktop.Screens.FirstOrDefault(s => s.Primary) : desktop.Screens.FirstOrDefault(s => s.DeviceName == displayDevice);
    int CurrentOffset
    {
        get => settings.DisplayOffsets.TryGetValue(TargetScreen?.DeviceName ?? "", out var value) ? value : settings.Offset;
        set { if (TargetScreen is { } screen) settings.DisplayOffsets[screen.DeviceName] = value; }
    }
    readonly bool preview;
    readonly bool verifyUi;
    readonly bool exitAfterVerify;
    readonly bool verifyMenu;
    int sampleCount;
    readonly NotifyIcon tray = null!;
    readonly ContextMenuStrip menu = null!;
    readonly ToolTip tip = new() { InitialDelay = 400, ReshowDelay = 100, AutoPopDelay = 15000 };
    readonly System.Windows.Forms.Timer positionTimer = new() { Interval = 120 };
    readonly System.Windows.Forms.Timer menuTimer = null!;
    bool IsMenuVisible => (host ?? this).menu?.Visible == true;
    int previousMouseButtons;
    IntPtr menuForeground;
    readonly CancellationTokenSource cancellation = new();
    Sample? sample;
    string status = "正在连接传感器…";
    bool dark = true, panelDark = true, dragging, quitting;
    Point dragStart;
    int offsetStart;
    Font valueFont = null!, labelFont = null!;
    Color foreground, muted, accent;
    readonly Icon appIcon;
    double? SelectedFan => settings.FanSource switch
    {
        "gpu" => sample?.GpuFanRpm,
        "max" => sample?.CpuFanRpm is { } cpu && sample.GpuFanRpm is { } gpu ? Math.Max(cpu, gpu) : sample?.CpuFanRpm ?? sample?.GpuFanRpm,
        _ => sample?.CpuFanRpm
    };
    protected override bool ShowWithoutActivation => !preview;
    protected override CreateParams CreateParams { get { var cp = base.CreateParams; if (!preview) cp.ExStyle |= 0x08080080; return cp; } }
    public MonitorForm(bool preview = false, bool verifyUi = false, bool exitAfterVerify = true, bool verifyMenu = false, MonitorForm? host = null, string? displayDevice = null, bool verifyDisplays = false, bool benchmark = false)
    {
        this.host = host; this.displayDevice = displayDevice; this.verifyDisplays = verifyDisplays;
        desktop = host?.desktop ?? new DesktopState();
        inventory = host?.inventory ?? new NetworkInventory();
        this.benchmark = benchmark;
        settings = host?.settings ?? Settings.Load();
        if (verifyDisplays) settings.DisplayMode = "all";
        if (benchmark) settings.DisplayMode = "all";
        if (verifyMenu) settings.DisplayMode = "primary";
        this.preview = preview;
        this.verifyUi = verifyUi;
        this.exitAfterVerify = exitAfterVerify;
        this.verifyMenu = verifyMenu;
        Text = "LegionBar"; FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Dpi; DoubleBuffered = true; TopMost = true;
        appIcon = host?.appIcon ?? CreateIcon(); Icon = appIcon;
        if (preview) { FormBorderStyle = FormBorderStyle.FixedSingle; ShowInTaskbar = true; StartPosition = FormStartPosition.CenterScreen; MaximizeBox = false; MinimizeBox = false; }
        if (host == null)
        {
            menuFont = SystemTypography.Create(9);
            menu = new ContextMenuStrip { AutoClose = true, Font = menuFont, Padding = new Padding(3, 6, 3, 6) };
            menuTimer = new System.Windows.Forms.Timer { Interval = 30 };
            tray = new NotifyIcon { Icon = appIcon, Text = "LegionBar", Visible = true };
            tray.MouseUp += (_, e) => { if (e.Button == MouseButtons.Right) ShowTrayMenu(); };
            BuildMenu();
            menu.Opening += (_, e) => { tip.Hide(this); tip.Active = false; PrepareMenu(); e.Cancel = false; };
            menu.Closed += (_, _) => { tip.Active = true; menuTimer.Stop(); };
            menuTimer.Tick += (_, _) => DismissMenuIfOutside();
        }
        MouseEnter += (_, _) => UpdateTooltip();
        MouseDown += (_, e) => { if (e.Button == MouseButtons.Left && !settings.Locked) { dragging = true; dragStart = Cursor.Position; offsetStart = CurrentOffset; Capture = true; } };
        MouseMove += (_, _) => { if (dragging) { CurrentOffset = offsetStart + (int)((Cursor.Position.X - dragStart.X) / (DeviceDpi / 96f)); PositionPanel(); } };
        MouseUp += (_, e) =>
        {
            if (dragging) { dragging = false; Capture = false; settings.Save(); }
        };
        positionTimer.Tick += (_, _) => Pulse();
        DpiChanged += (_, _) => { if (!quitting && IsHandleCreated) BeginInvoke(() => { if (!quitting) { ApplyTheme(); PositionPanel(); RefreshPanel(); } }); };
        Shown += (_, _) =>
        {
            if (host == null) SyncDisplays(); ApplyTheme(); PositionPanel(); if (preview) CenterToScreen();
            if (benchmark) { using var process = Process.GetCurrentProcess(); benchmarkStart = Stopwatch.GetTimestamp(); benchmarkCpu = process.TotalProcessorTime; }
            if (host == null) { positionTimer.Start(); StartSampling(); }
        };
    }
    void PrepareMenu()
    {
        using var measurement = PerformanceProbe.Measure("Menu.Prepare");
        if (menuDirty || menuNetworkVersion != inventory.Version || menuDesktopVersion != desktop.MenuVersion) BuildMenu();
        if (startupItem != null) startupItem.Checked = Settings.Startup;
    }
    void Pulse()
    {
        using var measurement = PerformanceProbe.Measure("UI.Pulse");
        desktop.Refresh(); inventory.RefreshIfDue(); SyncDisplays();
        ApplyTheme(); PositionPanel();
        foreach (var panel in displayPanels.Values) { panel.ApplyTheme(); panel.PositionPanel(); }
        if (Interlocked.Exchange(ref pendingSample, null) is { } next)
        {
            sample = next.Sample; status = next.Status;
            tray.Text = $"LegionBar | C {Display.Percent(sample.Cpu)} G {Display.Percent(sample.Gpu)} M {Display.Percent(sample.Memory)}";
            RefreshPanel();
            foreach (var panel in displayPanels.Values) { panel.sample = sample; panel.status = status; panel.RefreshPanel(); }
            if (verifyUi && ++sampleCount == 3) VerifyPanel();
        }
        if (benchmark && Stopwatch.GetElapsedTime(benchmarkStart).TotalSeconds >= 6) FinishBenchmark();
    }
    void FinishBenchmark()
    {
        var elapsed = Stopwatch.GetElapsedTime(benchmarkStart).TotalSeconds;
        using var process = Process.GetCurrentProcess();
        var cpu = (process.TotalProcessorTime - benchmarkCpu).TotalSeconds;
        PrepareMenu();
        var generation = menuGeneration;
        for (var i = 0; i < 20; i++) PrepareMenu();
        var cachedMenuReused = menuGeneration == generation;
        var interactiveMetrics = PerformanceProbe.Snapshot();
        var detailsRemoved = menu.Items.OfType<ToolStripMenuItem>().All(m => m.Text != "查看详情");
        var oldLock = settings.Locked;
        settings.Locked = !oldLock; menuDirty = true; PrepareMenu();
        var settingsReflected = menu.Items.OfType<ToolStripMenuItem>().First(m => m.Text?.StartsWith("锁定位置") == true).Checked == settings.Locked;
        settings.Locked = oldLock; menuDirty = true;
        process.Refresh();
        var result = new { ElapsedSeconds = elapsed, IdleCpuCorePercent = cpu * 100 / elapsed, WorkingSetMB = process.WorkingSet64 / 1048576.0, process.HandleCount, Checks = new { CachedMenuReused = cachedMenuReused, SettingsReflected = settingsReflected, DetailsRemoved = detailsRemoved }, Metrics = interactiveMetrics };
        if (!cachedMenuReused || !settingsReflected || !detailsRemoved) Environment.ExitCode = 7;
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "performance-report.json"), System.Text.Json.JsonSerializer.Serialize(result, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        Close();
    }
    void SyncDisplays(bool force = false)
    {
        if (!force && knownTopology == desktop.TopologyVersion) return;
        knownTopology = desktop.TopologyVersion; menuDirty = true;
        var screens = desktop.Screens;
        var primary = screens.FirstOrDefault(s => s.Primary) ?? screens.First();
        selectedScreens = DisplayTargets.Select(settings.DisplayMode, settings.SelectedDisplays, screens.Select(s => s.DeviceName).ToArray(), primary.DeviceName);
        foreach (var key in displayPanels.Keys.ToArray())
            if (!selectedScreens.Contains(key) || screens.All(s => s.DeviceName != key) || key == primary.DeviceName) { displayPanels[key].Close(); displayPanels.Remove(key); }
        foreach (var screen in screens.Where(s => !s.Primary && selectedScreens.Contains(s.DeviceName)))
        {
            if (displayPanels.ContainsKey(screen.DeviceName)) continue;
            var panel = new MonitorForm(host: this, displayDevice: screen.DeviceName);
            displayPanels[screen.DeviceName] = panel;
            panel.sample = sample; panel.status = status; panel.Show();
        }
    }
    void StartSampling()
    {
        var token = cancellation.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                using var sampler = new Sampler(inventory);
                while (!token.IsCancellationRequested)
                {
                    sampler.NetworkId = settings.NetworkId;
                    var next = sampler.Read(); var nextStatus = sampler.Status;
                    if (token.IsCancellationRequested) break;
                    Interlocked.Exchange(ref pendingSample, new PendingSample(next, nextStatus));
                    await Task.Delay(settings.Interval, token);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception e) { Program.Log(e); }
        }, token);
    }
    void UpdateTooltip()
    {
        if (sample == null) return;
        tip.SetToolTip(this, $"CPU  {Display.Percent(sample.Cpu)}   {Display.Temperature(sample.CpuTemperature)}\nGPU  {Display.Percent(sample.Gpu)}   {Display.Temperature(sample.GpuTemperature)}\n内存  {Display.Percent(sample.Memory)}\nCPU 风扇  {Display.Rpm(sample.CpuFanRpm)} RPM    GPU 风扇  {Display.Rpm(sample.GpuFanRpm)} RPM\n上传  {Display.Rate(sample.Upload)}    下载  {Display.Rate(sample.Download)}\n\n{status}\n\n托盘图标右键设置");
    }
    void ApplyTheme(bool? systemOverride = null)
    {
        using var measurement = PerformanceProbe.Measure("Theme.Check");
        var systemDark = systemOverride ?? desktop.SystemDark;
        bool next = settings.Theme != "light";
        if (settings.Theme == "auto") next = systemDark;
        if (valueFont != null && dark == next && panelDark == systemDark && Math.Abs(valueFont.Size - settings.FontSize) < .01) return;
        dark = next; panelDark = systemDark;
        BackColor = panelDark ? Color.FromArgb(30, 32, 36) : Color.FromArgb(242, 244, 247);
        foreground = panelDark ? Color.FromArgb(238, 240, 244) : Color.FromArgb(30, 34, 42);
        muted = panelDark ? Color.FromArgb(166, 176, 192) : Color.FromArgb(77, 87, 102);
        accent = panelDark ? Color.FromArgb(117, 207, 194) : Color.FromArgb(21, 121, 112);
        valueFont?.Dispose(); labelFont?.Dispose();
        valueFont = SystemTypography.Create(settings.FontSize);
        labelFont = SystemTypography.Create(settings.FontSize);
        lastVisualState = "";
        if (menu != null) { menuDirty = true; menu.Renderer = new MenuRenderer(dark); StyleMenu(menu); }
        RefreshPanel();
    }
    void StyleMenu(ToolStrip strip)
    {
        strip.SuspendLayout();
        var renderer = (MenuRenderer)menu.Renderer;
        strip.Renderer = renderer; strip.BackColor = renderer.Background; strip.ForeColor = renderer.Foreground;
        foreach (ToolStripItem item in strip.Items)
        {
            item.ForeColor = renderer.Foreground;
            if (item is ToolStripMenuItem m)
            {
                item.Padding = new Padding(2, 4, 6, 4);
                if (m.HasDropDownItems) StyleMenu(m.DropDown);
            }
        }
        strip.ResumeLayout(false);
    }
    int LogicalWidth => (settings.Compact ? 8 : 12) + (settings.Cpu ? (settings.Compact ? 42 : 91) : 0) + (settings.Gpu ? (settings.Compact ? 42 : 91) : 0) + (settings.Memory ? (settings.Compact ? 40 : 89) : 0) + (settings.Network ? (settings.Compact ? 90 : 113) : 0);
    void PositionPanel()
    {
        using var measurement = PerformanceProbe.Measure("Panel.Position");
        if (quitting) return;
        if (preview) { ClientSize = new Size((int)(LogicalWidth * DeviceDpi / 96f * settings.FontSize / 9f), (int)(40 * DeviceDpi / 96f * settings.FontSize / 9f)); return; }
        var screen = TargetScreen;
        if (screen == null || (host == null && !preview && !selectedScreens.Contains(screen.DeviceName))) { HidePanel(); return; }
        var bar = desktop.Taskbar(screen);
        if (bar == IntPtr.Zero) { HidePanel(); return; }
        if (!Native.GetWindowRect(bar, out var rect)) { desktop.Invalidate(); HidePanel(); return; }
        var bounds = rect.Rectangle;
        var fullscreen = desktop.IsFullscreen(screen);
        Native.DwmGetWindowAttribute(bar, 14, out var cloaked, 4);
        var scale = DeviceDpi / 96f * settings.FontSize / 9f;
        var width = Math.Min((int)(LogicalWidth * scale), bounds.Width);
        var height = Math.Min((int)(40 * scale), bounds.Height);
        if (!verifyMenu && !TaskbarVisibility.ShouldShow(Native.IsWindowVisible(bar), cloaked != 0, bounds, screen.Bounds, fullscreen))
        {
            ClientSize = new Size(width, height); HidePanel(); return;
        }
        var x = Math.Clamp(bounds.Left + (int)(CurrentOffset * DeviceDpi / 96f), bounds.Left, Math.Max(bounds.Left, bounds.Right - width));
        var y = bounds.Top + (bounds.Height - height) / 2;
        var changed = Bounds != new Rectangle(x, y, width, height) || !Visible;
        if (!IsMenuVisible && (changed || lastScenePositioned != desktop.SceneVersion))
        {
            PerformanceProbe.Count("Panel.SetWindowPos");
            Native.SetWindowPos(Handle, new IntPtr(-1), x, y, width, height, 0x0010 | 0x0040);
            lastScenePositioned = desktop.SceneVersion;
        }
        if (!Visible) Show();
        if (changed) RefreshPanel();
    }
    void HidePanel() { if (Visible) Hide(); }
    Bitmap RenderPanel(bool transparent)
    {
        var bitmap = new Bitmap(Math.Max(1, ClientSize.Width), Math.Max(1, ClientSize.Height), PixelFormat.Format32bppPArgb);
        bitmap.SetResolution(DeviceDpi, DeviceDpi);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(transparent ? Color.FromArgb(1, 0, 0, 0) : BackColor);
        DrawPanel(graphics);
        return bitmap;
    }
    void RefreshPanel()
    {
        using var measurement = PerformanceProbe.Measure("Panel.Refresh");
        if (!IsHandleCreated || valueFont == null || quitting || !Visible) return;
        if (preview) { Invalidate(); return; }
        var visual = $"{ClientSize}|{DeviceDpi}|{panelDark}|{settings.FontSize}|{settings.Compact}|{settings.Cpu}|{settings.Gpu}|{settings.Memory}|{settings.Network}|{settings.Temperatures}|{Display.Percent(sample?.Cpu)}|{Display.Percent(sample?.Gpu)}|{Display.Percent(sample?.Memory)}|{Display.Temperature(sample?.CpuTemperature)}|{Display.Temperature(sample?.GpuTemperature)}|{Display.Rpm(SelectedFan)}|{Display.Rate(sample?.Upload)}|{Display.Rate(sample?.Download)}";
        if (lastVisualState == visual) return;
        lastVisualState = visual; PerformanceProbe.Count("Panel.Render");
        using var bitmap = RenderPanel(true);
        LayeredPanel.Present(Handle, bitmap, Location);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        if (preview) { base.OnPaint(e); DrawPanel(e.Graphics); }
    }
    void DrawPanel(Graphics g)
    {
        if (valueFont == null) return;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        var scale = DeviceDpi / 96f * settings.FontSize / 9f;
        int P(float v) => (int)Math.Round(v * scale);
        var rowHeight = ClientSize.Height / 2;
        int x = P(settings.Compact ? 4 : 6);
        using var format = new StringFormat(StringFormat.GenericTypographic) { LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap };
        void Text(string text, Font font, Color color, int left, int top, int width) { using var brush = new SolidBrush(color); g.DrawString(text, font, brush, new RectangleF(left, top, width, rowHeight), format); }
        void Column(string label, string value, string bottom, double? temperature, int width, string bottomLabel = "")
        {
            var offset = settings.Compact ? 0 : 32;
            if (settings.Compact) width = label == "RAM" ? 40 : 42;
            if (!settings.Compact) { Text(label, labelFont, muted, x, 0, P(31)); Text(bottomLabel, labelFont, muted, x, rowHeight, P(31)); }
            Text(value, valueFont, foreground, x + P(offset), 0, P(width - offset - 5));
            Text(bottom, valueFont, temperature >= 85 ? Color.FromArgb(239, 147, 89) : foreground, x + P(offset), rowHeight, P(width - offset - 5));
            if (!settings.Compact)
            {
                using var pen = new Pen(panelDark ? Color.FromArgb(90, 95, 105) : Color.FromArgb(160, 168, 180));
                g.DrawLine(pen, x + P(width - 7), P(9), x + P(width - 7), ClientSize.Height - P(9));
            }
            x += P(width);
        }
        if (settings.Cpu) Column("CPU", Display.Percent(sample?.Cpu), settings.Temperatures ? Display.Temperature(sample?.CpuTemperature) : (settings.Compact ? "" : "占用"), sample?.CpuTemperature, 91);
        if (settings.Gpu) Column("GPU", Display.Percent(sample?.Gpu), settings.Temperatures ? Display.Temperature(sample?.GpuTemperature) : (settings.Compact ? "" : "占用"), sample?.GpuTemperature, 91);
        if (settings.Memory) Column("RAM", Display.Percent(sample?.Memory), Display.Rpm(SelectedFan), null, 89, "FAN");
        if (settings.Network)
        {
            var arrowWidth = settings.Compact ? 11 : 17; var rateWidth = settings.Compact ? 75 : 96;
            Text("↑", valueFont, accent, x, 0, P(arrowWidth)); Text(Display.Rate(sample?.Upload), valueFont, foreground, x + P(arrowWidth), 0, P(rateWidth));
            Text("↓", valueFont, accent, x, rowHeight, P(arrowWidth)); Text(Display.Rate(sample?.Download), valueFont, foreground, x + P(arrowWidth), rowHeight, P(rateWidth));
        }
    }
    ToolStripMenuItem Item(string text, Action click, bool? check = null)
    {
        var item = new ToolStripMenuItem(text); if (check.HasValue) item.Checked = check.Value;
        item.Click += (_, _) => { try { click(); } catch (Exception e) { MessageBox.Show(e.Message, "LegionBar"); } }; return item;
    }
    void ShowVerificationMenu()
    {
        var area = Screen.FromRectangle(Bounds).WorkingArea;
        var anchor = new Point(Left + 80, Math.Min(Top - (int)(3 * DeviceDpi / 96f), area.Bottom));
        ShowMenuAbove(anchor, area, anchor.Y);
    }
    void ShowTrayMenu()
    {
        var cursor = Cursor.Position;
        var area = Screen.FromPoint(cursor).WorkingArea;
        ShowMenuAbove(new Point(cursor.X, Math.Min(cursor.Y, area.Bottom)), area, area.Bottom);
    }
    void ShowMenuAbove(Point anchor, Rectangle area, int ceiling)
    {
        tip.Hide(this);
        if (menu.Visible) { menu.Close(); return; }
        Native.SetForegroundWindow(Handle);
        // Explicit direction avoids Windows choosing free space on the monitor below.
        menu.Show(this, PointToClient(anchor), ToolStripDropDownDirection.AboveRight);
        // Use the actual laid-out size, including the current monitor's DPI.
        menu.Location = MenuPlacement.AboveRight(anchor, menu.Size, area, ceiling);
        previousMouseButtons = MouseButtonsDown(); menuForeground = Native.GetForegroundWindow(); menuTimer.Start();
    }
    static int MouseButtonsDown() => (Native.GetAsyncKeyState(1) < 0 ? 1 : 0) | (Native.GetAsyncKeyState(2) < 0 ? 2 : 0) | (Native.GetAsyncKeyState(4) < 0 ? 4 : 0);
    bool InsideOpenMenu(ToolStripDropDown current, Point point)
    {
        if (current.Visible && current.Bounds.Contains(point)) return true;
        foreach (ToolStripMenuItem item in current.Items.OfType<ToolStripMenuItem>())
            if (item.HasDropDownItems && item.DropDown.Visible && InsideOpenMenu(item.DropDown, point)) return true;
        return false;
    }
    void DismissMenuIfOutside()
    {
        if (!menu.Visible) { menuTimer.Stop(); return; }
        var buttons = MouseButtonsDown();
        var pressed = buttons & ~previousMouseButtons; previousMouseButtons = buttons;
        var front = Native.GetForegroundWindow(); Native.GetWindowThreadProcessId(front, out var process);
        if ((pressed != 0 && !InsideOpenMenu(menu, Cursor.Position)) || (front != menuForeground && process != Environment.ProcessId)) menu.Close();
    }
    void Change(Action action)
    {
        action(); menuDirty = true; settings.Save(); SyncDisplays(true); ApplyTheme(); PositionPanel(); RefreshPanel();
        foreach (var panel in displayPanels.Values) { panel.ApplyTheme(); panel.PositionPanel(); panel.RefreshPanel(); }
    }
    void BuildMenu()
    {
        using var measurement = PerformanceProbe.Measure("Menu.Build");
        var currentNetworkVersion = inventory.Version;
        var currentDesktopVersion = desktop.MenuVersion;
        var networkSnapshot = inventory.Snapshot;
        menu.SuspendLayout();
        foreach (ToolStripItem item in menu.Items.Cast<ToolStripItem>().ToArray()) item.Dispose(); menu.Items.Clear();
        menu.Items.Add(new ToolStripMenuItem("LegionBar") { Enabled = false });
        menu.Items.Add(new ToolStripSeparator());
        var monitors = Group("显示器");
        monitors.DropDownItems.Add(Item("自动 · 仅主屏", () => Change(() => settings.DisplayMode = "primary"), settings.DisplayMode == "primary"));
        monitors.DropDownItems.Add(Item("所有显示器（自动包含新屏幕）", () => Change(() => settings.DisplayMode = "all"), settings.DisplayMode == "all"));
        monitors.DropDownItems.Add(new ToolStripSeparator());
        var screens = desktop.Screens;
        var primary = screens.FirstOrDefault(s => s.Primary) ?? screens.First();
        var chosen = DisplayTargets.Select(settings.DisplayMode, settings.SelectedDisplays, screens.Select(s => s.DeviceName).ToArray(), primary.DeviceName);
        for (var i = 0; i < screens.Length; i++)
        {
            var screen = screens[i];
            var hasTaskbar = desktop.Taskbar(screen) != IntPtr.Zero;
            var text = $"显示器 {i + 1}{(screen.Primary ? " · 主屏" : "")} · {screen.Bounds.Width}×{screen.Bounds.Height}{(hasTaskbar ? "" : " · 无任务栏")}";
            monitors.DropDownItems.Add(Item(text, () => Change(() =>
            {
                var next = new HashSet<string>(chosen);
                if (!next.Remove(screen.DeviceName)) next.Add(screen.DeviceName);
                if (next.Count == 0) return;
                settings.DisplayMode = "selected"; settings.SelectedDisplays = next.ToList();
            }), chosen.Contains(screen.DeviceName)));
        }
        menu.Items.Add(monitors);
        var display = Group("显示内容");
        void Toggle(string name, Func<bool> read, Action<bool> write)
        {
            display.DropDownItems.Add(Item(name, () => Change(() => { var next = !read(); if (!next && new[] { settings.Cpu, settings.Gpu, settings.Memory, settings.Network }.Count(x => x) == 1) return; write(next); }), read()));
        }
        Toggle("CPU 占用", () => settings.Cpu, v => settings.Cpu = v);
        Toggle("GPU 占用", () => settings.Gpu, v => settings.Gpu = v);
        Toggle("内存占用 / 风扇转速", () => settings.Memory, v => settings.Memory = v);
        Toggle("上传 / 下载", () => settings.Network, v => settings.Network = v);
        display.DropDownItems.Add(Item("CPU / GPU 温度", () => Change(() => settings.Temperatures = !settings.Temperatures), settings.Temperatures)); menu.Items.Add(display);
        var fans = Group("风扇转速来源（RPM）");
        foreach (var (name, value) in new[] { ("CPU 风扇", "cpu"), ("GPU 风扇", "gpu"), ("两者较高转速", "max") })
            fans.DropDownItems.Add(Item(name, () => Change(() => settings.FanSource = value), settings.FanSource == value));
        menu.Items.Add(fans);
        var refresh = Group("刷新间隔");
        foreach (var ms in new[] { 1000, 2000, 3000, 5000 }) refresh.DropDownItems.Add(Item($"{ms / 1000} 秒", () => Change(() => settings.Interval = ms), settings.Interval == ms)); menu.Items.Add(refresh);
        var theme = Group("外观");
        theme.DropDownItems.Add(Item("简约显示（隐藏标签和分隔线）", () => Change(() => settings.Compact = !settings.Compact), settings.Compact));
        theme.DropDownItems.Add(new ToolStripSeparator());
        foreach (var (name, value) in new[] { ("跟随系统", "auto"), ("深色", "dark"), ("浅色", "light") }) theme.DropDownItems.Add(Item(name, () => Change(() => settings.Theme = value), settings.Theme == value));
        theme.DropDownItems.Add(new ToolStripSeparator());
        foreach (var size in new[] { 8, 9, 10, 11 }) theme.DropDownItems.Add(Item($"字号 {size}", () => Change(() => settings.FontSize = size), settings.FontSize == size)); menu.Items.Add(theme);
        var network = Group("网络接口");
        network.DropDownItems.Add(Item("自动 · 活动以太网 / Wi-Fi", () => Change(() => settings.NetworkId = ""), settings.NetworkId == ""));
        foreach (var nic in networkSnapshot.Where(n => n.Type != NetworkInterfaceType.Loopback))
            network.DropDownItems.Add(Item(nic.Name + (nic.Status == OperationalStatus.Up ? " · 已连接" : " · 未连接"), () => Change(() => settings.NetworkId = nic.Id), settings.NetworkId == nic.Id)); menu.Items.Add(network);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Item("锁定位置（关闭后可拖动）", () => Change(() => settings.Locked = !settings.Locked), settings.Locked));
        menu.Items.Add(Item("重置到任务栏左侧", () => Change(() => { settings.Offset = 12; settings.DisplayOffsets.Clear(); })));
        startupItem = Item("开机启动", () => { Settings.Startup = !Settings.Startup; menuDirty = true; }, Settings.Startup);
        menu.Items.Add(startupItem);
        if (!LenovoSensors.IsAdministrator) menu.Items.Add(Item("启用温度和风扇（管理员）", Elevate));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Item("关于 LegionBar", ShowAbout));
        menu.Items.Add(Item("退出", Close));
        if (menu.Renderer is MenuRenderer) StyleMenu(menu);
        ResumeMenuLayouts(menu);
        menuNetworkVersion = currentNetworkVersion; menuDesktopVersion = currentDesktopVersion; menuDirty = false;
        menuGeneration++;
    }
    static ToolStripMenuItem Group(string text) { var item = new ToolStripMenuItem(text); item.DropDown.SuspendLayout(); return item; }
    static void ResumeMenuLayouts(ToolStrip strip)
    {
        foreach (var item in strip.Items.OfType<ToolStripMenuItem>()) if (item.HasDropDownItems) ResumeMenuLayouts(item.DropDown);
        strip.ResumeLayout(true);
    }
    void Elevate()
    {
        try
        {
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!, "--handoff") { UseShellExecute = true, Verb = "runas", WorkingDirectory = AppContext.BaseDirectory });
            Close();
        }
        catch (System.ComponentModel.Win32Exception e) when (e.NativeErrorCode == 1223) { }
    }
    void ShowAbout()
    {
        using var form = new Form { Text = "LegionBar", ClientSize = new Size(440, 205), StartPosition = FormStartPosition.CenterScreen, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, Icon = appIcon, BackColor = BackColor, ForeColor = foreground };
        form.Controls.Add(new Label { Location = new Point(20, 20), Size = new Size(400, 125), Font = new Font("Microsoft YaHei UI", 9), Text = "LegionBar 1.5.1\nCopyright © 2026 Ktclat and contributors\nGNU AGPL v3.0 only · 按现状提供，不提供担保。\n原创黑白图标；非联想官方工具，无厂商背书。\n许可证及第三方声明随便携程序分发。" });
        var source = new LinkLabel { Text = "源代码与许可证", Location = new Point(20, 155), AutoSize = true, LinkColor = accent };
        source.LinkClicked += (_, _) => Process.Start(new ProcessStartInfo("https://github.com/Ktclat/LegionBar") { UseShellExecute = true });
        form.Controls.Add(source); form.ShowDialog();
    }
    void VerifyPanel()
    {
        if (verifyDisplays)
        {
            var screens = Screen.AllScreens;
            var panels = new[] { this }.Concat(displayPanels.Values).ToArray();
            var report = new
            {
                ScreenCount = screens.Length, PanelCount = panels.Length, SamplingWorkers = panels.Count(p => p.host == null), TrayIcons = panels.Count(p => p.tray?.Visible == true),
                Displays = screens.Select(s => new { s.DeviceName, s.Primary, s.Bounds, HasTaskbar = Taskbars.Find(s) != IntPtr.Zero }),
                Panels = panels.Select(p => new { Device = p.TargetScreen?.DeviceName, p.Visible, p.Bounds, p.DeviceDpi, Font = p.valueFont?.Name, Size = p.valueFont?.Size, HasSample = p.sample != null })
            };
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "displays-verification.json"), System.Text.Json.JsonSerializer.Serialize(report, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            if (panels.Length != screens.Length) Environment.ExitCode = 6;
        }
        using var bitmap = RenderPanel(true);
        bitmap.Save(Path.Combine(AppContext.BaseDirectory, "panel-preview.png"));
        var taskbar = Native.FindWindow("Shell_TrayWnd", null); Native.GetWindowRect(taskbar, out var rect);
        var state = new { Visible, Bounds, Taskbar = rect.Rectangle, DeviceDpi, HasSample = sample != null, FitsTaskbar = rect.Rectangle.Contains(Bounds), Transparent = bitmap.GetPixel(0, 0).A == 1, settings.Compact, status };
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "ui-verification.json"), System.Text.Json.JsonSerializer.Serialize(state, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        if (verifyMenu)
        {
            ShowVerificationMenu();
            var area = Screen.FromRectangle(Bounds).WorkingArea;
            var result = new { MenuBounds = menu.Bounds, PanelBounds = Bounds, WorkingArea = area, IsAbove = menu.Bottom <= Top, FitsScreen = area.Contains(menu.Bounds), menu.Visible };
            if (!result.IsAbove || !result.FitsScreen || !result.Visible) Environment.ExitCode = 4;
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "menu-verification.json"), System.Text.Json.JsonSerializer.Serialize(result, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            using var renderedMenu = new Bitmap(menu.Width, menu.Height);
            menu.DrawToBitmap(renderedMenu, new Rectangle(Point.Empty, menu.Size));
            renderedMenu.Save(Path.Combine(AppContext.BaseDirectory, "menu-preview.png"));
            menu.Close();
            var originalTheme = settings.Theme; var originalCompact = settings.Compact;
            foreach (var variant in new[] { "dark", "light" })
            {
                settings.Theme = variant; ApplyTheme(variant == "dark");
                ShowVerificationMenu();
                using (var snapshot = new Bitmap(menu.Width, menu.Height))
                {
                    menu.DrawToBitmap(snapshot, new Rectangle(Point.Empty, menu.Size));
                    snapshot.Save(Path.Combine(AppContext.BaseDirectory, $"menu-{variant}.png"));
                }
                if (menu.Items.OfType<ToolStripMenuItem>().FirstOrDefault(m => m.Text == "外观") is { } appearance)
                {
                    appearance.ShowDropDown();
                    using var submenu = new Bitmap(appearance.DropDown.Width, appearance.DropDown.Height);
                    appearance.DropDown.DrawToBitmap(submenu, new Rectangle(Point.Empty, submenu.Size));
                    submenu.Save(Path.Combine(AppContext.BaseDirectory, $"submenu-{variant}.png"));
                }
                menu.Close();
                foreach (var compact in new[] { false, true })
                {
                    settings.Compact = compact; PositionPanel();
                    using var panel = RenderPanel(true);
                    panel.Save(Path.Combine(AppContext.BaseDirectory, $"panel-{variant}-{(compact ? "compact" : "normal")}.png"));
                }
            }
            settings.Theme = originalTheme; settings.Compact = originalCompact; ApplyTheme(); PositionPanel();
        }
        if (exitAfterVerify) Close();
    }
    static Icon CreateIcon()
    {
        using var stream = typeof(MonitorForm).Assembly.GetManifestResourceStream("LegionBar.assets.LegionBar.ico") ?? throw new InvalidOperationException("LegionBar icon resource is missing.");
        using var icon = new Icon(stream, new Size(32, 32));
        return (Icon)icon.Clone();
    }
    protected override void WndProc(ref Message m)
    {
        if (!preview && m.Msg == 0x21) { m.Result = new IntPtr(3); return; }
        if (m.Msg == 0x7B)
        {
            // Settings are intentionally available only through the tray icon.
            return;
        }
        base.WndProc(ref m);
    }
    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        quitting = true;
        foreach (var panel in displayPanels.Values.ToArray()) panel.Close();
        displayPanels.Clear();
        cancellation.Cancel(); positionTimer.Stop(); positionTimer.Dispose(); menuTimer?.Dispose(); tray?.Dispose(); menu?.Dispose(); menuFont?.Dispose(); tip.Dispose(); valueFont?.Dispose(); labelFont?.Dispose();
        if (host == null) { desktop.Dispose(); inventory.Dispose(); appIcon.Dispose(); }
        base.OnFormClosed(e);
    }
}
