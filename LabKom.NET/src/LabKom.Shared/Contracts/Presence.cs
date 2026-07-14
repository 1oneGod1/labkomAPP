namespace LabKom.Shared.Contracts;

/// <summary>
/// Status koneksi Student Agent dari sudut pandang Teacher Console.
/// </summary>
public enum StudentStatus
{
    Offline = 0,
    Online = 1,
    LoggedIn = 2,
    Locked = 3,
}

/// <summary>
/// Payload yang dikirim Student Agent saat connect ke Teacher Hub
/// dan saat heartbeat berkala (HelloAsync, HeartbeatAsync).
/// </summary>
public sealed record StudentPresence(
    string PcName,
    string MacAddress,
    string IpAddress,
    string? StudentNis,
    string? StudentName,
    StudentStatus Status,
    long TimestampUnixMs)
{
    public static StudentPresence Snapshot(
        string pcName,
        string mac,
        string ip,
        StudentStatus status,
        string? nis = null,
        string? name = null) => new(
            pcName,
            mac,
            ip,
            nis,
            name,
            status,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
}

/// <summary>
/// Event presence yang di-push Teacher Hub ke Teacher Console UI.
/// </summary>
public sealed record PresenceUpdate(
    string PcName,
    StudentStatus Status,
    StudentPresence? Snapshot,
    long TimestampUnixMs);
