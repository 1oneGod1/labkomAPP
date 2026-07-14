using LabKom.Shared.Discovery;

namespace LabKom.Student.Services;

/// <summary>
/// Menyimpan endpoint Teacher Console terbaru yang diterima via UDP discovery.
/// Thread-safe karena diakses oleh DiscoveryWorker dan HubConnectionService.
/// </summary>
public class TeacherEndpointStore
{
    private readonly object _gate = new();
    private DiscoveryBeacon? _current;
    private DateTime _lastSeenUtc;

    public event EventHandler<DiscoveryBeacon>? TeacherDiscovered;

    public DiscoveryBeacon? Current
    {
        get { lock (_gate) return _current; }
    }

    public bool IsFresh
    {
        get
        {
            lock (_gate)
            {
                if (_current is null) return false;
                return (DateTime.UtcNow - _lastSeenUtc).TotalSeconds < DiscoveryProtocol.StaleAfterSeconds;
            }
        }
    }

    public void Update(DiscoveryBeacon beacon)
    {
        bool changed;
        lock (_gate)
        {
            changed = _current is null
                      || _current.Ip != beacon.Ip
                      || _current.HubPort != beacon.HubPort
                      || _current.TeacherId != beacon.TeacherId;
            _current = beacon;
            _lastSeenUtc = DateTime.UtcNow;
        }
        if (changed) TeacherDiscovered?.Invoke(this, beacon);
    }

    public string? BuildHubUrl()
    {
        var beacon = Current;
        if (beacon is null) return null;
        return $"http://{beacon.Ip}:{beacon.HubPort}{LabKom.Shared.Hub.HubRoutes.TeacherHubPath}";
    }
}
