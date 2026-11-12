using System.Net.NetworkInformation;
namespace LegionBar;

internal sealed record AdapterInfo(NetworkInterface Adapter, string Id, string Name, string Description, NetworkInterfaceType Type, OperationalStatus Status);

internal sealed class NetworkInventory : IDisposable
{
    AdapterInfo[] snapshot = [];
    int refreshing, version, invalidated = 1;
    long nextRefresh;
    volatile bool disposed;
    public AdapterInfo[] Snapshot => Volatile.Read(ref snapshot);
    public int Version => Volatile.Read(ref version);
    public NetworkInventory()
    {
        NetworkChange.NetworkAddressChanged += Changed;
        NetworkChange.NetworkAvailabilityChanged += AvailabilityChanged;
        RefreshIfDue();
    }
    void Changed(object? sender, EventArgs e) { Volatile.Write(ref invalidated, 1); Interlocked.Exchange(ref nextRefresh, 0); RefreshIfDue(); }
    void AvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e) => Changed(sender, e);
    public void RefreshIfDue()
    {
        if (disposed || Environment.TickCount64 < Interlocked.Read(ref nextRefresh) || Interlocked.CompareExchange(ref refreshing, 1, 0) != 0) return;
        Volatile.Write(ref invalidated, 0);
        _ = Task.Run(() =>
        {
            try
            {
                var next = NetworkInterface.GetAllNetworkInterfaces().Select(n => new AdapterInfo(n, n.Id, n.Name, n.Description, n.NetworkInterfaceType, n.OperationalStatus)).ToArray();
                var previous = Snapshot;
                var changed = !previous.Select(n => (n.Id, n.Name, n.Status)).SequenceEqual(next.Select(n => (n.Id, n.Name, n.Status)));
                if (!disposed) { Volatile.Write(ref snapshot, next); if (changed) Interlocked.Increment(ref version); }
            }
            catch (NetworkInformationException) { }
            finally
            {
                var again = Volatile.Read(ref invalidated) != 0;
                Interlocked.Exchange(ref nextRefresh, again ? 0 : Environment.TickCount64 + 5000);
                Volatile.Write(ref refreshing, 0);
                if (again) RefreshIfDue();
            }
        });
    }
    public void Dispose()
    {
        disposed = true;
        NetworkChange.NetworkAddressChanged -= Changed;
        NetworkChange.NetworkAvailabilityChanged -= AvailabilityChanged;
    }
}
