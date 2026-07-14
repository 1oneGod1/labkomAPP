namespace LabKom.Shared.Discovery;

/// <summary>
/// Konstanta protokol auto-discovery LAN.
/// Teacher Console broadcast presence-nya ke port ini setiap interval,
/// Student Agent listen dan menyimpan endpoint Teacher untuk koneksi SignalR.
/// </summary>
public static class DiscoveryProtocol
{
    public const int Port = 41234;
    public const string Magic = "LABKOM";
    public const int Version = 1;
    public const int BroadcastIntervalSeconds = 2;
    public const int StaleAfterSeconds = 10;
}

/// <summary>
/// Payload yang dikirim Teacher Console via UDP broadcast.
/// Student Agent men-deserialize ini untuk menemukan Teacher di subnet yang sama.
/// </summary>
public sealed record DiscoveryBeacon(
    string Magic,
    int Version,
    string TeacherId,
    string TeacherName,
    string Ip,
    int HubPort,
    long TimestampUnixMs)
{
    public static DiscoveryBeacon Create(string teacherId, string teacherName, string ip, int hubPort) => new(
        DiscoveryProtocol.Magic,
        DiscoveryProtocol.Version,
        teacherId,
        teacherName,
        ip,
        hubPort,
        DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

    public bool IsValid() =>
        Magic == DiscoveryProtocol.Magic &&
        Version == DiscoveryProtocol.Version &&
        !string.IsNullOrWhiteSpace(Ip) &&
        HubPort > 0;
}
