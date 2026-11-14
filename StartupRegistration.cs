using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Xml.Linq;
using Microsoft.Win32;

namespace LegionBar;

internal sealed record StartupState(bool Known, bool Enabled, bool Scheduled, bool Legacy, bool NeedsRepair, string? ExecutablePath = null, string? Error = null, bool ConfigurationValid = false);

internal sealed class StartupRegistration
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    static readonly XNamespace Ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
    public static string UserSid { get; } = GetSid();
    public static string TaskName => "LegionBar_Autorun_" + UserSid;
    readonly string taskName, legacyKey, legacyName;
    readonly string expectedArguments;
    readonly bool requiresLogon;

    public StartupRegistration() : this(TaskName, RunKey, "LegionBar", "--startup", true) { }
    StartupRegistration(string taskName, string legacyKey, string legacyName, string expectedArguments, bool requiresLogon)
        => (this.taskName, this.legacyKey, this.legacyName, this.expectedArguments, this.requiresLogon) = (taskName, legacyKey, legacyName, expectedArguments, requiresLogon);
    static string GetSid() { using var identity = WindowsIdentity.GetCurrent(); return identity.User?.Value ?? throw new InvalidOperationException("无法识别当前 Windows 用户。"); }
    static bool Missing(Exception error) => unchecked((uint)error.HResult) is 0x80070002 or 0x80070003;

    public StartupState Read()
    {
        using var key = Registry.CurrentUser.OpenSubKey(legacyKey);
        var legacy = key?.GetValue(legacyName) != null;
        using var com = new SchedulerSession();
        dynamic? task = com.Find(taskName);
        if (task == null) return new(true, legacy, false, legacy, legacy);
        var document = XDocument.Parse((string)task.Xml);
        var path = document.Descendants(Ns + "Command").FirstOrDefault()?.Value;
        var samePath = string.Equals(path, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase);
        var elevated = document.Descendants(Ns + "RunLevel").FirstOrDefault()?.Value == "HighestAvailable";
        var interactive = document.Descendants(Ns + "LogonType").FirstOrDefault()?.Value == "InteractiveToken";
        var sameUser = IsCurrentUser(document.Descendants(Ns + "Principal").Elements(Ns + "UserId").FirstOrDefault()?.Value);
        var logon = document.Descendants(Ns + "LogonTrigger").FirstOrDefault();
        var triggerMatches = !requiresLogon || (IsCurrentUser(logon?.Element(Ns + "UserId")?.Value) && logon?.Element(Ns + "Delay")?.Value == "PT10S");
        var argumentsMatch = document.Descendants(Ns + "Arguments").FirstOrDefault()?.Value == expectedArguments;
        var enabled = (bool)task.Enabled;
        var valid = enabled && samePath && elevated && interactive && sameUser && triggerMatches && argumentsMatch;
        return new(true, enabled || legacy, enabled, legacy, legacy || !valid, path, ConfigurationValid: valid);
    }
    static bool IsCurrentUser(string? user)
    {
        if (user == UserSid) return true;
        if (string.IsNullOrWhiteSpace(user)) return false;
        try { return ((SecurityIdentifier)new NTAccount(user).Translate(typeof(SecurityIdentifier))).Value == UserSid; }
        catch (IdentityNotMappedException) { return false; }
    }

    public void SetEnabled(bool enabled)
    {
        if (!LenovoSensors.IsAdministrator) throw new UnauthorizedAccessException("修改管理员开机启动需要管理员权限。");
        if (enabled) Enable(Environment.ProcessPath ?? throw new InvalidOperationException("找不到当前程序路径。"), "--startup", true);
        else
        {
            using var com = new SchedulerSession();
            if (com.Find(taskName) != null) com.Folder.DeleteTask(taskName, 0);
            RemoveLegacy();
        }
    }
    void Enable(string executable, string arguments, bool logon)
    {
        if (!File.Exists(executable)) throw new FileNotFoundException("启动程序不存在。", executable);
        using (var com = new SchedulerSession())
            com.Keep(com.Folder.RegisterTask(taskName, MakeXml(executable, UserSid, arguments, logon), 6, UserSid, null, 3, null));
        var state = Read();
        if (!state.Scheduled || !state.ConfigurationValid) throw new InvalidOperationException("计划任务未能正确注册为当前用户的最高权限交互任务。");
        // Preserve the old autorun until task registration has succeeded.
        RemoveLegacy();
    }
    void RemoveLegacy() { using var key = Registry.CurrentUser.OpenSubKey(legacyKey, true); key?.DeleteValue(legacyName, false); }
    public void Run()
    {
        using var com = new SchedulerSession();
        dynamic task = com.Find(taskName) ?? throw new InvalidOperationException("开机启动任务不存在。");
        com.Keep(task.Run(null));
    }

    internal static string MakeXml(string executable, string sid, string arguments = "--startup", bool logon = true)
    {
        var doc = new XDocument(new XElement(Ns + "Task", new XAttribute("version", "1.2"),
            new XElement(Ns + "RegistrationInfo", new XElement(Ns + "Author", "LegionBar"), new XElement(Ns + "Description", "LegionBar 当前用户登录后以最高权限启动，读取温度与风扇。")),
            new XElement(Ns + "Triggers", logon ? new XElement(Ns + "LogonTrigger", new XElement(Ns + "Enabled", true), new XElement(Ns + "UserId", sid), new XElement(Ns + "Delay", "PT10S")) : null),
            new XElement(Ns + "Principals", new XElement(Ns + "Principal", new XAttribute("id", "CurrentUser"), new XElement(Ns + "UserId", sid), new XElement(Ns + "LogonType", "InteractiveToken"), new XElement(Ns + "RunLevel", "HighestAvailable"))),
            new XElement(Ns + "Settings", new XElement(Ns + "MultipleInstancesPolicy", "IgnoreNew"), new XElement(Ns + "DisallowStartIfOnBatteries", false), new XElement(Ns + "StopIfGoingOnBatteries", false), new XElement(Ns + "AllowHardTerminate", true), new XElement(Ns + "StartWhenAvailable", true), new XElement(Ns + "RunOnlyIfNetworkAvailable", false), new XElement(Ns + "AllowStartOnDemand", true), new XElement(Ns + "Enabled", true), new XElement(Ns + "ExecutionTimeLimit", "PT0S"), new XElement(Ns + "Priority", 7)),
            new XElement(Ns + "Actions", new XAttribute("Context", "CurrentUser"), new XElement(Ns + "Exec", new XElement(Ns + "Command", executable), new XElement(Ns + "Arguments", arguments), new XElement(Ns + "WorkingDirectory", Path.GetDirectoryName(executable))))));
        return doc.ToString();
    }

    public static Dictionary<string, bool> VerifyXmlCases()
    {
        const string path = @"C:\Tools & Apps\拯救者\LegionBar.exe";
        var xml = XDocument.Parse(MakeXml(path, "S-1-5-21-123-456-789-1001"));
        string? Value(string name) => xml.Descendants(Ns + name).FirstOrDefault()?.Value;
        return new()
        {
            ["StartupUsesHighestPrivileges"] = Value("RunLevel") == "HighestAvailable",
            ["StartupUsesInteractiveUserSession"] = Value("LogonType") == "InteractiveToken",
            ["StartupHasCurrentUserLogonTrigger"] = Value("UserId") == "S-1-5-21-123-456-789-1001" && Value("Delay") == "PT10S",
            ["StartupAllowsBatteryAndUnlimitedRuntime"] = Value("DisallowStartIfOnBatteries") == "false" && Value("StopIfGoingOnBatteries") == "false" && Value("ExecutionTimeLimit") == "PT0S",
            ["StartupEscapesPathsWithoutShellCommands"] = Value("Command") == path && Value("Arguments") == "--startup",
            ["StartupIgnoresDuplicateInstances"] = Value("MultipleInstancesPolicy") == "IgnoreNew"
        };
    }

    public static object VerifyIntegration()
    {
        if (!LenovoSensors.IsAdministrator) throw new UnauthorizedAccessException("启动任务集成检查需要管理员权限。");
        var id = Guid.NewGuid().ToString("N");
        var keyPath = @"Software\LegionBar\StartupTests\" + id;
        var outputDir = Path.Combine(Path.GetTempPath(), "LegionBarStartupTest_" + id);
        Directory.CreateDirectory(outputDir);
        var output = Path.Combine(outputDir, "sensors.json");
        var arguments = "--diagnose --require-sensors --output \"" + output + "\"";
        var registration = new StartupRegistration("LegionBar_StartupSelfTest_" + id, keyPath, "Legacy", arguments, false);
        try
        {
            using (var key = Registry.CurrentUser.CreateSubKey(keyPath)) key.SetValue("Legacy", "test-only");
            registration.Enable(Environment.ProcessPath!, arguments, false);
            var enabled = registration.Read();
            registration.Run();
            var deadline = Environment.TickCount64 + 30000;
            bool completed = false;
            while (Environment.TickCount64 < deadline)
            {
                using var com = new SchedulerSession();
                dynamic task = com.Find(registration.taskName)!;
                if (File.Exists(output) && (int)task.State != 4 && (int)task.LastTaskResult == 0) { completed = true; break; }
                Thread.Sleep(150);
            }
            if (!completed) throw new InvalidOperationException("测试任务未能完成传感器诊断。");
            using var sensors = System.Text.Json.JsonDocument.Parse(File.ReadAllText(output));
            var checks = sensors.RootElement.GetProperty("Checks");
            var validSensors = checks.GetProperty("CpuTemperature").GetBoolean() && checks.GetProperty("CpuFan").GetBoolean() && checks.GetProperty("GpuFan").GetBoolean();
            registration.SetEnabled(false);
            var disabled = registration.Read();
            if (!enabled.Scheduled || enabled.Legacy || !validSensors || disabled.Enabled) throw new InvalidOperationException("计划任务注册、迁移或清理检查失败。");
            return new { Registered = true, LegacyMigrated = true, ElevatedSensorReadings = validSensors, DisabledAndRemoved = !disabled.Enabled, ProductionStartupUntouched = true };
        }
        finally
        {
            using (var com = new SchedulerSession())
            {
                dynamic? task = com.Find(registration.taskName);
                if (task != null) { if ((int)task.State == 4) task.Stop(0); com.Folder.DeleteTask(registration.taskName, 0); }
            }
            using (var key = Registry.CurrentUser.OpenSubKey(keyPath, true)) key?.DeleteValue("Legacy", false);
            Registry.CurrentUser.DeleteSubKey(keyPath, false);
            if (File.Exists(output)) File.Delete(output);
            Directory.Delete(outputDir, false);
        }
    }

    sealed class SchedulerSession : IDisposable
    {
        readonly List<object> objects = new();
        public dynamic Folder { get; }
        public SchedulerSession()
        {
            try
            {
                dynamic service = Keep(Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service") ?? throw new InvalidOperationException("Windows 任务计划程序不可用。"))!);
                service.Connect(); Folder = Keep(service.GetFolder("\\"));
            }
            catch { Dispose(); throw; }
        }
        public dynamic Keep(object item) { if (Marshal.IsComObject(item) && !objects.Any(existing => ReferenceEquals(existing, item))) objects.Add(item); return item; }
        public dynamic? Find(string name) { try { return Keep(Folder.GetTask(name)); } catch (Exception e) when (Missing(e)) { return null; } }
        public void Dispose() { for (var i = objects.Count - 1; i >= 0; i--) Marshal.ReleaseComObject(objects[i]); objects.Clear(); }
    }
}
