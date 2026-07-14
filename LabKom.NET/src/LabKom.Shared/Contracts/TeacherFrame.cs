namespace LabKom.Shared.Contracts;

/// <summary>
/// Frame dari layar guru yang di-broadcast ke semua siswa saat mode
/// Screen Broadcast aktif. Berbeda dari ScreenFrame (siswa → guru) karena
/// dialiri ke overlay client, bukan ke registry.
/// </summary>
public sealed record TeacherFrame(
    int Width,
    int Height,
    byte[] JpegData,
    long TimestampUnixMs)
{
    public static TeacherFrame Create(int w, int h, byte[] jpeg) =>
        new(w, h, jpeg, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
}

/// <summary>
/// Sinyal mulai/berhenti broadcast layar guru.
/// </summary>
public sealed record TeacherBroadcastSignal(bool Active);
