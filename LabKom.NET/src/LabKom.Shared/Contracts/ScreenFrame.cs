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
/// Frame screen yang dikirim Student → Teacher Hub via SignalR.
/// Image disimpan sebagai byte[] JPEG untuk hemat bandwidth.
/// </summary>
public sealed record ScreenFrame(
    string PcName,
    CaptureProfile Profile,
    int Width,
    int Height,
    byte[] JpegData,
    long TimestampUnixMs)
{
    public static ScreenFrame Create(
        string pcName,
        CaptureProfile profile,
        int width,
        int height,
        byte[] jpeg) => new(
            pcName,
            profile,
            width,
            height,
            jpeg,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
}

/// <summary>
/// Perintah dari Teacher ke satu Student: ubah profile capture (overview/focus).
/// </summary>
public sealed record CaptureProfileCommand(CaptureProfile Profile);
