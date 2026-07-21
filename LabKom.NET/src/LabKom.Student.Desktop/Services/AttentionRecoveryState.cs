namespace LabKom.Student.Desktop.Services;

public enum AttentionRecoveryReason
{
    TeacherOfflineTimeout,
    MaximumLockDuration,
    EmergencyAdministratorUnlock,
}

public sealed record AttentionRecoveryDecision(
    AttentionRecoveryReason Reason,
    string? CommandId,
    DateTimeOffset TriggeredAtUtc,
    string Description);

/// <summary>Thread-safe timing state for fail-safe release of Attention mode.</summary>
public sealed class AttentionRecoveryState
{
    private readonly object _sync = new();
    private bool _attentionActive;
    private bool _teacherConnected;
    private DateTimeOffset? _lockedSinceUtc;
    private DateTimeOffset? _offlineSinceUtc;
    private string? _commandId;

    public bool IsAttentionActive
    {
        get
        {
            lock (_sync) return _attentionActive;
        }
    }

    public void SetAttention(
        bool active,
        string? commandId,
        DateTimeOffset now)
    {
        lock (_sync)
        {
            if (!active)
            {
                ResetLock();
                return;
            }

            if (!_attentionActive)
            {
                _lockedSinceUtc = now;
                _offlineSinceUtc = _teacherConnected ? null : now;
            }

            _attentionActive = true;
            _commandId = commandId;
        }
    }

    public void ObserveTeacherConnection(bool connected, DateTimeOffset now)
    {
        lock (_sync)
        {
            if (_teacherConnected == connected) return;
            _teacherConnected = connected;
            if (!_attentionActive) return;
            _offlineSinceUtc = connected ? null : now;
        }
    }

    public AttentionRecoveryDecision? Evaluate(
        DateTimeOffset now,
        TimeSpan maximumLockDuration,
        TimeSpan teacherOfflineTimeout)
    {
        lock (_sync)
        {
            if (!_attentionActive) return null;

            if (maximumLockDuration > TimeSpan.Zero
                && _lockedSinceUtc is { } lockedSince
                && now - lockedSince >= maximumLockDuration)
            {
                return Recover(
                    AttentionRecoveryReason.MaximumLockDuration,
                    now,
                    "Durasi maksimum lock tercapai");
            }

            if (!_teacherConnected
                && teacherOfflineTimeout > TimeSpan.Zero
                && _offlineSinceUtc is { } offlineSince
                && now - offlineSince >= teacherOfflineTimeout)
            {
                return Recover(
                    AttentionRecoveryReason.TeacherOfflineTimeout,
                    now,
                    "Koneksi Teacher hilang melewati recovery timeout");
            }

            return null;
        }
    }

    public AttentionRecoveryDecision ForceEmergency(
        DateTimeOffset now,
        string description)
    {
        lock (_sync)
        {
            return Recover(
                AttentionRecoveryReason.EmergencyAdministratorUnlock,
                now,
                description);
        }
    }

    private AttentionRecoveryDecision Recover(
        AttentionRecoveryReason reason,
        DateTimeOffset now,
        string description)
    {
        var decision = new AttentionRecoveryDecision(
            reason,
            _commandId,
            now,
            description);
        ResetLock();
        return decision;
    }

    private void ResetLock()
    {
        _attentionActive = false;
        _lockedSinceUtc = null;
        _offlineSinceUtc = null;
        _commandId = null;
    }
}
