using System.Diagnostics;

namespace LegionBar;

// Task Scheduler COM calls never run in the menu-opening/UI thread.
internal sealed class StartupManager : IDisposable
{
    readonly SemaphoreSlim gate = new(1);
    StartupState state = new(false, false, false, false, false);
    int refreshing;
    long nextRefresh;
    volatile bool disposed;
    public StartupState State => Volatile.Read(ref state);
    public StartupManager() => RefreshIfDue(true);
    public void RefreshIfDue(bool force = false)
    {
        if (disposed || (!force && Environment.TickCount64 < Interlocked.Read(ref nextRefresh)) || Interlocked.CompareExchange(ref refreshing, 1, 0) != 0) return;
        _ = Task.Run(async () =>
        {
            await gate.WaitAsync();
            try
            {
                if (!disposed) Volatile.Write(ref state, ReadState());
            }
            finally { Interlocked.Exchange(ref nextRefresh, Environment.TickCount64 + 5000); gate.Release(); Volatile.Write(ref refreshing, 0); }
        });
    }
    static StartupState ReadState()
    {
        try { return new StartupRegistration().Read(); }
        catch (Exception error) { Program.Log(error); return new(false, false, false, false, false, Error: error.Message); }
    }
    public Task<bool> SetEnabledAsync(bool enabled) => Task.Run(async () =>
    {
        await gate.WaitAsync();
        try
        {
            if (LenovoSensors.IsAdministrator) new StartupRegistration().SetEnabled(enabled);
            else
            {
                var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas", WorkingDirectory = AppContext.BaseDirectory };
                start.ArgumentList.Add("--startup-config"); start.ArgumentList.Add(enabled ? "enable" : "disable");
                start.ArgumentList.Add("--expected-sid"); start.ArgumentList.Add(StartupRegistration.UserSid);
                if (enabled) start.ArgumentList.Add("--start-after-enable");
                using var helper = Process.Start(start) ?? throw new InvalidOperationException("未能启动管理员配置程序。");
                await helper.WaitForExitAsync();
                if (helper.ExitCode != 0) throw new InvalidOperationException("修改开机启动失败，请使用同一 Windows 管理员账户重试；详细原因已记录到 errors.log。");
            }
            Volatile.Write(ref state, ReadState());
            return enabled && !LenovoSensors.IsAdministrator;
        }
        finally { Interlocked.Exchange(ref nextRefresh, 0); gate.Release(); }
    });
    public void Dispose() => disposed = true;
}
