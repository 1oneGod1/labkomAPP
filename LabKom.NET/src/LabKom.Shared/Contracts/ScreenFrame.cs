namespace LabKom.Shared.Contracts;

/// <summary>
/// Profile capture: thumbnail (low-rate) untuk grid, focus (high-rate) saat admin watch satu PC.
/// </summary>
public enum CaptureProfile
{
    Thumbnail = 0,
    Focus = 1,
}

/// <summary>
/// Frame layar yang dikirim Student Desktop ke Teacher melalui SignalR.
/// StreamId dan SequenceNumber mencegah frame lama menggantikan frame baru.
/// </summary>
public sealed record ScreenFrame(
    string PcName,
    CaptureProfile Profile,
    string MonitorId,
    string StreamId,
    int Width,
    int Height,
    byte[] JpegData,
    long SequenceNumber,
    long TimestampUnixMs)
{
    public static ScreenFrame Create(
        string pcName,
        CaptureProfile profile,
        string monitorId,
        string streamId,
        int width,
        int height,
        byte[] jpeg,
        long sequenceNumber) => new(
            pcName,
            profile,
            monitorId,
            streamId,
            width,
            height,
            jpeg,
            sequenceNumber,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
}

/// <summary>Perintah Teacher untuk memilih profile dan monitor capture siswa.</summary>
public sealed record CaptureProfileCommand(CaptureProfile Profile, string? MonitorId = null);

/// <summary>Deskripsi satu monitor pada desktop interaktif siswa.</summary>
public sealed record MonitorDescriptor(
    string Id,
    string DeviceName,
    int Left,
    int Top,
    int Width,
    int Height,
    bool IsPrimary);

/// <summary>Inventaris monitor yang tersedia pada satu PC siswa.</summary>
public sealed record MonitorInventory(
    string PcName,
    IReadOnlyList<MonitorDescriptor> Monitors,
    long TimestampUnixMs)
{
    public static MonitorInventory Snapshot(string pcName, IReadOnlyList<MonitorDescriptor> monitors) =>
        new(pcName, monitors, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
}
