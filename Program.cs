using System.Text.Json;
namespace LegionBar;
internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if (args.Contains("--self-test-layout"))
        {
            var cases = FullscreenDetection.VerifyCases();
            foreach (var pair in DisplayTargets.VerifyCases()) cases[pair.Key] = pair.Value;
            cases["TaskbarVisibility"] = TaskbarVisibility.VerifyCases();
            var path = args.SkipWhile(x => x != "--output").Skip(1).FirstOrDefault() ?? Path.Combine(AppContext.BaseDirectory, "layout-check.json");
            File.WriteAllText(path, JsonSerializer.Serialize(cases, new JsonSerializerOptions { WriteIndented = true }));
            if (cases.Values.Any(passed => !passed)) Environment.ExitCode = 5;
            return;
        }
        if (args.Contains("--verify-menu"))
        {
            Application.Run(new MonitorForm(verifyUi: true, verifyMenu: true));
            return;
        }
        if (args.Contains("--verify-displays"))
        {
            Application.Run(new MonitorForm(verifyUi: true, verifyDisplays: true));
            return;
        }
        if (args.Contains("--diagnose") || args.Contains("--self-test"))
        {
            using var sampler = new Sampler(); sampler.Read(); Thread.Sleep(1200); var s = sampler.Read();
            var checks = new { Cpu = s.Cpu is >= 0 and <= 100, Memory = s.Memory is > 0 and <= 100, Network = s.Upload >= 0 && s.Download >= 0, Format = Display.Rate(1024) == "1.0 K/s", CpuTemperature = s.CpuTemperature is > 0 and < 130, CpuFan = s.CpuFanRpm is >= 0 and <= 15000, GpuFan = s.GpuFanRpm is >= 0 and <= 15000 };
            var result = new { Sample = s, sampler.Status, ProcessMemoryMB = Environment.WorkingSet / 1048576, Checks = checks, TaskbarVisibility = TaskbarVisibility.VerifyCases() };
            var path = args.SkipWhile(x => x != "--output").Skip(1).FirstOrDefault() ?? Path.Combine(AppContext.BaseDirectory, "diagnostics.json");
            File.WriteAllText(path, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
            if (args.Contains("--self-test") && !(checks.Cpu && checks.Memory && checks.Network && checks.Format)) Environment.ExitCode = 1;
            if (args.Contains("--require-sensors") && !(checks.CpuTemperature && checks.CpuFan && checks.GpuFan)) Environment.ExitCode = 2;
            if (!TaskbarVisibility.VerifyCases()) Environment.ExitCode = 3;
            return;
        }
        Mutex mutex;
        try { mutex = new Mutex(false, "Local\\LegionBar.Singleton"); }
        catch (UnauthorizedAccessException) { return; } // An elevated instance may already own it.
        using (mutex)
        {
            bool acquired;
            try { acquired = mutex.WaitOne(args.Contains("--handoff") ? TimeSpan.FromSeconds(10) : TimeSpan.Zero); }
            catch (AbandonedMutexException) { acquired = true; }
            if (!acquired) return;
            try
            {
                Application.ThreadException += (_, e) => Log(e.Exception);
                AppDomain.CurrentDomain.UnhandledException += (_, e) => Log(e.ExceptionObject as Exception);
                Application.Run(new MonitorForm(args.Contains("--preview"), args.Contains("--verify-ui") || args.Contains("--capture-ui"), !args.Contains("--capture-ui")));
            }
            finally { mutex.ReleaseMutex(); }
        }
    }
    internal static void Log(Exception? e)
    {
        try { Directory.CreateDirectory(Settings.DirectoryPath); File.AppendAllText(Path.Combine(Settings.DirectoryPath, "errors.log"), $"{DateTime.Now:O} {e}\n"); } catch { }
    }
}
internal static class Display
{
    public static string Percent(double? v) => v.HasValue ? $"{v:0}%" : "--";
    public static string Temperature(double? v) => v.HasValue ? $"{v:0}°C" : "--°C";
    public static string Rpm(double? v) => v.HasValue ? $"{v:0}" : "--";
    public static string Rate(double? v) => !v.HasValue ? "--" : v >= 1073741824 ? $"{v / 1073741824:0.0} G/s" : v >= 1048576 ? $"{v / 1048576:0.0} M/s" : $"{v / 1024:0.0} K/s";
}
