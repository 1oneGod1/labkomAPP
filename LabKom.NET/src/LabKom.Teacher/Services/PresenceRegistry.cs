using System.Collections.Concurrent;
using LabKom.Shared.Contracts;

namespace LabKom.Teacher.Services;

/// <summary>
/// Registry student yang sedang connect ke hub. Diakses oleh TeacherHub
/// (write) dan UI ViewModel (read via event PresenceChanged / FrameUpdated).
/// </summary>
public class PresenceRegistry
{
    private readonly ConcurrentDictionary<string, StudentEntry> _byPc = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _connToPc = new();

    public event EventHandler<PresenceUpdate>? PresenceChanged;
    public event EventHandler<ChatMessage>? ChatReceived;
    public event EventHandler<ScreenFrame>? FrameUpdated;

    public IReadOnlyCollection<StudentEntry> Snapshot() => _byPc.Values.ToArray();

    public StudentEntry? Get(string pcName) =>
        _byPc.TryGetValue(pcName, out var entry) ? entry : null;

    public void Upsert(StudentPresence p, string connectionId)
    {
        _connToPc[connectionId] = p.PcName;
        var entry = _byPc.AddOrUpdate(p.PcName,
            _ => new StudentEntry(p.PcName, p, StudentStatus.Online, DateTime.UtcNow, connectionId, null),
            (_, existing) => existing with { Presence = p, Status = p.Status, LastSeenUtc = DateTime.UtcNow, ConnectionId = connectionId });

        Raise(new PresenceUpdate(entry.PcName, entry.Status, p, p.TimestampUnixMs));
    }

    public void UpdateStatus(string connectionId, StudentStatus status)
    {
        if (!_connToPc.TryGetValue(connectionId, out var pc)) return;
        if (!_byPc.TryGetValue(pc, out var entry)) return;
        var updated = entry with { Status = status, LastSeenUtc = DateTime.UtcNow };
        _byPc[pc] = updated;
        Raise(new PresenceUpdate(pc, status, updated.Presence, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
    }

    public void MarkDisconnected(string connectionId)
    {
        if (!_connToPc.TryRemove(connectionId, out var pc)) return;
        if (!_byPc.TryGetValue(pc, out var entry)) return;
        var updated = entry with { Status = StudentStatus.Offline, LastSeenUtc = DateTime.UtcNow, LastFrame = null };
        _byPc[pc] = updated;
        Raise(new PresenceUpdate(pc, StudentStatus.Offline, updated.Presence, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
    }

    public void UpdateFrame(ScreenFrame frame)
    {
        if (!_byPc.TryGetValue(frame.PcName, out var entry)) return;
        _byPc[frame.PcName] = entry with { LastFrame = frame, LastSeenUtc = DateTime.UtcNow };
        FrameUpdated?.Invoke(this, frame);
    }

    public void RecordChat(ChatMessage message)
    {
        ChatReceived?.Invoke(this, message);
    }

    private void Raise(PresenceUpdate update) => PresenceChanged?.Invoke(this, update);
}

public sealed record StudentEntry(
    string PcName,
    StudentPresence Presence,
    StudentStatus Status,
    DateTime LastSeenUtc,
    string ConnectionId,
    ScreenFrame? LastFrame);
