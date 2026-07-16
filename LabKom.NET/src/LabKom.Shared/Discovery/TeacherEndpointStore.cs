using LabKom.Shared.Hub;

namespace LabKom.Shared.Discovery;

public sealed record TeacherEndpointSnapshot(
    DiscoveryBeacon Beacon,
    string HubUrl,
    DateTime LastSeenUtc);

/// <summary>
/// Thread-safe store untuk endpoint Teacher terbaru. Snapshot menyatukan URL dan
/// certificate pin agar reconnect tidak mencampur dua beacon yang berbeda.
/// </summary>
public sealed class TeacherEndpointStore
{
    private readonly object _gate = new();
    private DiscoveryBeacon? _current;
    private DateTime _lastSeenUtc;

    public event EventHandler<DiscoveryBeacon>? TeacherDiscovered;

    public DiscoveryBeacon? Current
    {
        get
        {
            lock (_gate) return _current;
        }
    }

    public bool IsFresh => GetFreshSnapshot() is not null;

    public TeacherEndpointSnapshot? GetFreshSnapshot()
    {
        lock (_gate)
        {
            if (_current is null
                || (DateTime.UtcNow - _lastSeenUtc).TotalSeconds >= DiscoveryProtocol.StaleAfterSeconds)
            {
                return null;
            }

            return new TeacherEndpointSnapshot(
                _current,
                BuildHubUrl(_current),
                _lastSeenUtc);
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
                      || _current.TeacherId != beacon.TeacherId
                      || _current.CertificateSha256 != beacon.CertificateSha256;
            _current = beacon;
            _lastSeenUtc = DateTime.UtcNow;
        }

        if (changed) TeacherDiscovered?.Invoke(this, beacon);
    }

    public string? BuildHubUrl()
    {
        lock (_gate)
        {
            return _current is null ? null : BuildHubUrl(_current);
        }
    }

    private static string BuildHubUrl(DiscoveryBeacon beacon) =>
        $"https://{beacon.Ip}:{beacon.HubPort}{HubRoutes.TeacherHubPath}";
}
