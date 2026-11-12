using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
namespace LegionBar;
internal sealed record Sample(DateTime Time, double? Cpu, double? Gpu, double? Memory, double? CpuTemperature, double? GpuTemperature, double? Upload, double? Download, double UsedGB, double TotalGB, double? CpuFanRpm, double? GpuFanRpm);
internal sealed class Sampler : IDisposable
{
    long idle, kernel, user;
    IntPtr query, counter, gpu;
    bool nvml;
    readonly LenovoSensors legion = new();
    string sensorStatus = "未初始化", gpuStatus = "未初始化";
    readonly Dictionary<string, (long sent, long received, long time)> previous = new();
    public string NetworkId { get; set; } = "";
    public string Status => $"{sensorStatus}\n权限：{(LenovoSensors.IsAdministrator ? "管理员" : "普通用户")}\nGPU：{gpuStatus}\n网络：{(string.IsNullOrEmpty(NetworkId) ? "活动以太网 / Wi-Fi 合计" : NetworkId)}\nCPU：Windows Processor Utility（不可用时用系统时间）";
    public Sampler()
    {
        Native.GetSystemTimes(out idle, out kernel, out user);
        if (Native.PdhOpenQuery(null, IntPtr.Zero, out query) == 0)
        {
            if (Native.PdhAddEnglishCounter(query, @"\Processor Information(_Total)\% Processor Utility", IntPtr.Zero, out counter) != 0) counter = IntPtr.Zero;
            Native.PdhCollectQueryData(query);
        }
        try { nvml = Native.nvmlInit_v2() == 0; if (nvml) Native.nvmlDeviceGetHandleByIndex_v2(0, out gpu); gpuStatus = gpu != IntPtr.Zero ? "NVIDIA NVML" : "NVIDIA 接口不可用"; }
        catch (Exception e) { gpuStatus = e is DllNotFoundException ? "未检测到 NVIDIA 驱动" : e.Message; }
    }
    public Sample Read()
    {
        double? cpu = null, memory = null, gpuTemp = null, gpuLoad = null;
        if (Native.GetSystemTimes(out var i, out var k, out var u))
        {
            var total = k - kernel + u - user;
            if (total > 0) cpu = Math.Clamp(100.0 * (total - (i - idle)) / total, 0, 100);
            (idle, kernel, user) = (i, k, u);
        }
        if (counter != IntPtr.Zero && Native.PdhCollectQueryData(query) == 0 && Native.PdhGetFormattedCounterValue(counter, 0x200, out _, out var v) == 0 && v.Status <= 1 && double.IsFinite(v.Value)) cpu = Math.Clamp(v.Value, 0, 100);
        var mem = new Native.MemoryStatus { Length = (uint)Marshal.SizeOf<Native.MemoryStatus>() };
        var memOk = Native.GlobalMemoryStatusEx(ref mem);
        if (memOk && mem.TotalPhysical > 0) memory = 100.0 * (mem.TotalPhysical - mem.AvailablePhysical) / mem.TotalPhysical;
        if (gpu != IntPtr.Zero)
        {
            var loadResult = Native.nvmlDeviceGetUtilizationRates(gpu, out var usage);
            var tempResult = Native.nvmlDeviceGetTemperature(gpu, 0, out var t);
            if (loadResult == 0) gpuLoad = Math.Clamp(usage.Gpu, 0, 100);
            if (tempResult == 0 && t is > 0 and < 130) gpuTemp = t;
            gpuStatus = loadResult == 0 ? "NVIDIA NVML · 第 1 块 NVIDIA GPU" : $"GPU 暂不可用（休眠 / 驱动状态 {loadResult}）";
        }
        var sensors = legion.Read(); sensorStatus = sensors.Status;
        var (upload, download) = ReadNetwork();
        return new(DateTime.Now, cpu, gpuLoad, memory, sensors.CpuTemperature, gpuTemp, upload, download,
            memOk ? (mem.TotalPhysical - mem.AvailablePhysical) / 1073741824.0 : 0, memOk ? mem.TotalPhysical / 1073741824.0 : 0, sensors.CpuFanRpm, sensors.GpuFanRpm);
    }
    (double?, double?) ReadNetwork()
    {
        double up = 0, down = 0; var seen = new HashSet<string>(); var available = false;
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up || (NetworkId != "" ? nic.Id != NetworkId : nic.NetworkInterfaceType is not (NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211))) continue;
            if (NetworkId == "" && (nic.Description.Contains("Virtual", StringComparison.OrdinalIgnoreCase) || nic.Description.Contains("TAP", StringComparison.OrdinalIgnoreCase) || nic.Description.Contains("VPN", StringComparison.OrdinalIgnoreCase))) continue;
            try
            {
                var stats = nic.GetIPStatistics(); var now = Stopwatch.GetTimestamp(); seen.Add(nic.Id); available = true;
                if (previous.TryGetValue(nic.Id, out var old))
                {
                    var seconds = (now - old.time) / (double)Stopwatch.Frequency;
                    if (seconds > 0 && seconds < 20) { up += Math.Max(0, stats.BytesSent - old.sent) / seconds; down += Math.Max(0, stats.BytesReceived - old.received) / seconds; }
                }
                previous[nic.Id] = (stats.BytesSent, stats.BytesReceived, now);
            }
            catch (NetworkInformationException) { }
        }
        foreach (var key in previous.Keys.Where(k => !seen.Contains(k)).ToArray()) previous.Remove(key);
        return available ? (up, down) : (null, null);
    }
    public void Dispose() { legion.Dispose(); if (query != IntPtr.Zero) Native.PdhCloseQuery(query); if (nvml) Native.nvmlShutdown(); }
}
