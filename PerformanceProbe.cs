using System.Diagnostics;
namespace LegionBar;

internal static class PerformanceProbe
{
    public static bool Enabled;
    static readonly Dictionary<string, (long Count, long Ticks, long Max)> values = new();
    public static Scope Measure(string name) => new(name);
    public static void Count(string name) { if (Enabled) Add(name, 0); }
    static void Add(string name, long ticks)
    {
        values.TryGetValue(name, out var old); values[name] = (old.Count + 1, old.Ticks + ticks, Math.Max(old.Max, ticks));
    }
    public readonly struct Scope : IDisposable
    {
        readonly string name; readonly long start;
        public Scope(string name) { this.name = name; start = Enabled ? Stopwatch.GetTimestamp() : 0; }
        public void Dispose() { if (start != 0) Add(name, Stopwatch.GetTimestamp() - start); }
    }
    public static object Snapshot() => values.ToDictionary(x => x.Key, x => new { x.Value.Count, TotalMs = x.Value.Ticks * 1000.0 / Stopwatch.Frequency, AverageMs = x.Value.Ticks * 1000.0 / Stopwatch.Frequency / x.Value.Count, MaxMs = x.Value.Max * 1000.0 / Stopwatch.Frequency });
}
