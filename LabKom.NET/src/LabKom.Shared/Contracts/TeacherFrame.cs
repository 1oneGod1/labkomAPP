namespace LabKom.Shared.Contracts;

/// <summary>Frame binary dari layar Teacher untuk satu sesi broadcast.</summary>
public sealed record TeacherFrame(
    string BroadcastId,
    long SequenceNumber,
    int Width,
    int Height,
    byte[] JpegData,
    long TimestampUnixMs)
{
    public static TeacherFrame Create(
        string broadcastId,
        long sequenceNumber,
        int width,
        int height,
        byte[] jpeg) => new(
            broadcastId,
            sequenceNumber,
            width,
            height,
            jpeg,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
}

/// <summary>State satu sesi broadcast Teacher.</summary>
public sealed record TeacherBroadcastSignal(
    string BroadcastId,
    bool Active,
    bool Paused);
