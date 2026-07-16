using System.Collections.Concurrent;
using LabKom.Shared.Contracts;

namespace LabKom.Teacher.Services;

/// <summary>
/// Thread-safe registry untuk Agent presence dan koneksi Desktop interaktif.
/// Koneksi pengganti selalu menang terhadap disconnect/frame terlambat dari koneksi lama.
/// </summary>
public sealed class PresenceRegistry
{
    private readonly ConcurrentDictionary<string, StudentEntry> _byPc =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _agentConnectionToPc = new();
    private readonly ConcurrentDictionary<string, string> _desktopConnectionToPc = new();
    private readonly ConcurrentDictionary<string, string> _pendingDesktopByPc =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, MonitorInventory> _pendingInventories =
        new(StringComparer.OrdinalIgnoreCase);

    public event EventHandler<PresenceUpdate>? PresenceChanged;
    public event EventHandler<ChatMessage>? ChatReceived;
    public event EventHandler<ScreenFrame>? FrameUpdated;
    public event EventHandler<MonitorInventory>? MonitorInventoryUpdated;

    public IReadOnlyCollection<StudentEntry> Snapshot() => _byPc.Values.ToArray();

    public StudentEntry? Get(string pcName) =>
        _byPc.TryGetValue(pcName, out var entry) ? entry : null;

    public void Upsert(StudentPresence presence, string connectionId)
    {
        _agentConnectionToPc[connectionId] = presence.PcName;
        _pendingDesktopByPc.TryGetValue(presence.PcName, out var pendingDesktop);
        _pendingInventories.TryRemove(presence.PcName, out var pendingInventory);

        var entry = _byPc.AddOrUpdate(
            presence.PcName,
            _ => new StudentEntry(
                presence.PcName,
                presence,
                presence.Status,
                DateTime.UtcNow,
                connectionId,
                pendingDesktop,
                null,
                pendingInventory),
            (_, current) => current with
            {
                Presence = presence,
                Status = presence.Status,
                LastSeenUtc = DateTime.UtcNow,
                ConnectionId = connectionId,
                DesktopConnectionId = pendingDesktop ?? current.DesktopConnectionId,
                MonitorInventory = pendingInventory ?? current.MonitorInventory,
            });

        Raise(new PresenceUpdate(entry.PcName, entry.Status, presence, presence.TimestampUnixMs));
        if (pendingInventory is not null)
        {
            MonitorInventoryUpdated?.Invoke(this, pendingInventory);
        }
    }

    public void RegisterDesktop(string pcName, string connectionId)
    {
        _desktopConnectionToPc[connectionId] = pcName;
        _pendingDesktopByPc[pcName] = connectionId;

        while (_byPc.TryGetValue(pcName, out var current))
        {
            var updated = current with
            {
                DesktopConnectionId = connectionId,
                LastFrame = null,
                MonitorInventory = null,
            };
            if (_byPc.TryUpdate(pcName, updated, current)) return;
        }
    }

    public void UpdateStatus(string connectionId, StudentStatus status)
    {
        if (!_agentConnectionToPc.TryGetValue(connectionId, out var pcName)) return;
        if (!_byPc.TryGetValue(pcName, out var current) || current.ConnectionId != connectionId) return;

        var updated = current with { Status = status, LastSeenUtc = DateTime.UtcNow };
        if (!_byPc.TryUpdate(pcName, updated, current)) return;

        Raise(new PresenceUpdate(
            pcName,
            status,
            updated.Presence,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
    }

    public void MarkAgentDisconnected(string connectionId)
    {
        if (!_agentConnectionToPc.TryRemove(connectionId, out var pcName)) return;
        if (!_byPc.TryGetValue(pcName, out var current) || current.ConnectionId != connectionId) return;

        var updated = current with
        {
            Status = StudentStatus.Offline,
            LastSeenUtc = DateTime.UtcNow,
            LastFrame = null,
            MonitorInventory = null,
        };
        if (!_byPc.TryUpdate(pcName, updated, current)) return;

        Raise(new PresenceUpdate(
            pcName,
            StudentStatus.Offline,
            updated.Presence,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
    }

    public void MarkDesktopDisconnected(string connectionId)
    {
        if (!_desktopConnectionToPc.TryRemove(connectionId, out var pcName)) return;
        if (_pendingDesktopByPc.TryGetValue(pcName, out var pending)
            && string.Equals(pending, connectionId, StringComparison.Ordinal))
        {
            _pendingDesktopByPc.TryRemove(pcName, out _);
        }

        while (_byPc.TryGetValue(pcName, out var current))
        {
            if (!string.Equals(current.DesktopConnectionId, connectionId, StringComparison.Ordinal)) return;
            var updated = current with { DesktopConnectionId = null };
            if (_byPc.TryUpdate(pcName, updated, current)) return;
        }
    }

    public bool UpdateFrame(ScreenFrame frame, string connectionId)
    {
        while (_byPc.TryGetValue(frame.PcName, out var current))
        {
            if (!string.Equals(current.DesktopConnectionId, connectionId, StringComparison.Ordinal))
            {
                return false;
            }

            var last = current.LastFrame;
            if (last is not null
                && string.Equals(last.StreamId, frame.StreamId, StringComparison.Ordinal)
                && frame.SequenceNumber <= last.SequenceNumber)
            {
                return false;
            }

            var updated = current with { LastFrame = frame, LastSeenUtc = DateTime.UtcNow };
            if (!_byPc.TryUpdate(frame.PcName, updated, current)) continue;

            FrameUpdated?.Invoke(this, frame);
            return true;
        }

        return false;
    }

    public bool UpdateMonitorInventory(MonitorInventory inventory, string connectionId)
    {
        if (!_desktopConnectionToPc.TryGetValue(connectionId, out var connectedPc)
            || !string.Equals(connectedPc, inventory.PcName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        while (_byPc.TryGetValue(inventory.PcName, out var current))
        {
            if (!string.Equals(current.DesktopConnectionId, connectionId, StringComparison.Ordinal))
            {
                return false;
            }

            var updated = current with { MonitorInventory = inventory, LastSeenUtc = DateTime.UtcNow };
            if (!_byPc.TryUpdate(inventory.PcName, updated, current)) continue;

            MonitorInventoryUpdated?.Invoke(this, inventory);
            return true;
        }

        _pendingInventories[inventory.PcName] = inventory;
        return true;
    }

    public void RecordChat(ChatMessage message) => ChatReceived?.Invoke(this, message);

    private void Raise(PresenceUpdate update) => PresenceChanged?.Invoke(this, update);
}

public sealed record StudentEntry(
    string PcName,
    StudentPresence Presence,
    StudentStatus Status,
    DateTime LastSeenUtc,
    string ConnectionId,
    string? DesktopConnectionId,
    ScreenFrame? LastFrame,
    MonitorInventory? MonitorInventory);
