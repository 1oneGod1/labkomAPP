using LabKom.Shared.Contracts;

namespace LabKom.Teacher.Services;

/// <summary>Identitas stabil untuk satu proses Teacher dan pembuat snapshot reconnect.</summary>
public sealed class ClassroomSessionIdentity
{
    public string SessionId { get; } = Guid.NewGuid().ToString("N");

    public ClassroomStateSnapshot CreateSnapshot(
        AttentionCommand? attention,
        TeacherBroadcastSignal? broadcast) =>
        ClassroomStateSnapshot.Create(SessionId, attention, broadcast);
}