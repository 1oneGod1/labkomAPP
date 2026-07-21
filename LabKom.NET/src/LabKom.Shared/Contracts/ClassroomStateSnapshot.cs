namespace LabKom.Shared.Contracts;

/// <summary>
/// Snapshot atomik state kelas yang dikirim saat Desktop terhubung kembali.
/// Nilai null berarti mode tersebut harus dinonaktifkan pada Student.
/// </summary>
public sealed record ClassroomStateSnapshot(
    string SessionId,
    AttentionCommand? Attention,
    TeacherBroadcastSignal? Broadcast,
    long TimestampUnixMs)
{
    public static ClassroomStateSnapshot Create(
        string sessionId,
        AttentionCommand? attention,
        TeacherBroadcastSignal? broadcast) => new(
            sessionId,
            attention,
            broadcast,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
}