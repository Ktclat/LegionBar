using System.Management;
using System.Security.Principal;

namespace LegionBar;

internal sealed record LegionReading(double? CpuTemperature, double? CpuFanRpm, double? GpuFanRpm, string Status);

// Only sensor getters are invoked here; no fan or firmware settings are written.
internal sealed class LenovoSensors : IDisposable
{
    readonly Endpoint fan = new("LENOVO_FAN_METHOD");
    readonly Endpoint gameZone = new("LENOVO_GAMEZONE_DATA");
    public static bool IsAdministrator { get; } = GetAdministrator();
    static bool GetAdministrator() { using var identity = WindowsIdentity.GetCurrent(); return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator); }

    public LegionReading Read()
    {
        var temperature = Valid(fan.Read("Fan_GetCurrentSensorTemperature", "CurrentSensorTemperature", "SensorID", 3), 1, 129);
        var source = "联想 Fan Sensor 3";
        if (temperature == null)
        {
            temperature = Valid(gameZone.Read("GetCPUTemp", "Data"), 1, 129);
            source = "联想 GameZone GetCPUTemp";
        }
        var cpuFan = Valid(fan.Read("Fan_GetCurrentFanSpeed", "CurrentFanSpeed", "FanID", 0), 0, 15000);
        var gpuFan = Valid(fan.Read("Fan_GetCurrentFanSpeed", "CurrentFanSpeed", "FanID", 1), 0, 15000);
        var hint = !IsAdministrator && (fan.AccessDenied || gameZone.AccessDenied)
            ? "需要管理员权限：右键 → 启用温度和风扇（管理员）"
            : string.Join("；", new[] { fan.Error, gameZone.Error }.Where(x => !string.IsNullOrEmpty(x)));
        var status = $"CPU 温度：{(temperature.HasValue ? source : "不可用")}\n风扇：CPU {(cpuFan.HasValue ? cpuFan + " RPM" : "--")} / GPU {(gpuFan.HasValue ? gpuFan + " RPM" : "--")}";
        if (!string.IsNullOrEmpty(hint)) status += "\n" + hint;
        return new(temperature, cpuFan, gpuFan, status);
    }

    static double? Valid(double? value, double minimum, double maximum) => value is { } v && double.IsFinite(v) && v >= minimum && v <= maximum ? value : null;
    public void Dispose() { fan.Dispose(); gameZone.Dispose(); }

    sealed class Endpoint(string className) : IDisposable
    {
        ManagementObject? instance;
        DateTime retryAt;
        public string Error { get; private set; } = "";
        public bool AccessDenied { get; private set; }

        public double? Read(string method, string outputName, string? inputName = null, byte inputValue = 0)
        {
            if (DateTime.UtcNow < retryAt) return null;
            try
            {
                if (instance == null)
                {
                    var scope = new ManagementScope(@"\\.\root\WMI", new ConnectionOptions
                    {
                        EnablePrivileges = true,
                        Impersonation = ImpersonationLevel.Impersonate,
                        Authentication = AuthenticationLevel.PacketPrivacy,
                        Timeout = TimeSpan.FromSeconds(3)
                    });
                    using var search = new ManagementObjectSearcher(scope, new ObjectQuery($"SELECT * FROM {className}"), new System.Management.EnumerationOptions { Timeout = TimeSpan.FromSeconds(3) });
                    using var found = search.Get();
                    foreach (ManagementObject item in found) { instance = item; break; }
                    if (instance == null) { Error = className + "：无实例"; retryAt = DateTime.UtcNow.AddMinutes(1); return null; }
                }
                using var input = instance.GetMethodParameters(method);
                if (inputName != null) input[inputName] = inputValue;
                using var output = instance.InvokeMethod(method, input, new InvokeMethodOptions { Timeout = TimeSpan.FromSeconds(3) });
                if (output?[outputName] == null) { Error = $"{className}.{method} 未返回 {outputName}"; return null; }
                Error = ""; AccessDenied = false;
                return Convert.ToDouble(output[outputName]);
            }
            catch (Exception e)
            {
                AccessDenied = e is UnauthorizedAccessException || e is ManagementException { ErrorCode: ManagementStatus.AccessDenied } || (uint)e.HResult is 0x80041003 or 0x80070005;
                Error = $"{className}.{method}：{e.Message} (0x{e.HResult:X8})";
                instance?.Dispose(); instance = null; retryAt = DateTime.UtcNow.AddSeconds(30);
                return null;
            }
        }
        public void Dispose() => instance?.Dispose();
    }
}
