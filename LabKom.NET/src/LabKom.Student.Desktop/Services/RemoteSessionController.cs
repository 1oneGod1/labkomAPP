using LabKom.Shared.Contracts;
using LabKom.Shared.Devices;

namespace LabKom.Student.Desktop.Services;

public readonly record struct AcceptedRemoteInput(
    RemoteSessionCommand Session,
    RemoteInputCommand Input);

/// <summary>
/// State machine sesi remote Student. Menolak target salah, input replay,
/// input view-only, input kedaluwarsa, dan sesi yang sudah dilepas lokal.
/// </summary>
public sealed class RemoteSessionController
{
    private readonly object _gate = new();
    private readonly string _pcName;
    private RemoteSessionCommand? _current;
    private long _lastInputSequence;
    private long _acceptedInputCount;

    public RemoteSessionController(MachineIdentity identity)
    {
        _pcName = identity.PcName;
    }

    public event EventHandler<RemoteSessionStatus>? StatusChanged;

    public RemoteSessionCommand? Current
    {
        get
        {
            lock (_gate)
            {
                return IsCurrentAlive(DateTimeOffset.UtcNow)
                    ? _current
                    : null;
            }
        }
    }

    public bool TryApply(RemoteSessionCommand command)
    {
        var now = DateTimeOffset.UtcNow;
        if (!ContractValidation.IsValidRemoteSessionCommand(
                command,
                _pcName,
                now.ToUnixTimeMilliseconds()))
        {
            return false;
        }

        RemoteSessionStatus? status;
        lock (_gate)
        {
            if (!command.Active)
            {
                if (_current is null
                    || !string.Equals(
                        _current.SessionId,
                        command.SessionId,
                        StringComparison.Ordinal))
                {
                    return false;
                }

                status = BuildStatus(
                    RemoteSessionState.Ended,
                    command.Reason ?? "Dihentikan Teacher");
                Clear();
            }
            else
            {
                var isRenewal = string.Equals(
                    _current?.SessionId,
                    command.SessionId,
                    StringComparison.Ordinal);
                _current = command;
                if (!isRenewal)
                {
                    _lastInputSequence = 0;
                    _acceptedInputCount = 0;
                }

                status = BuildStatus(RemoteSessionState.Active, null);
            }
        }

        StatusChanged?.Invoke(this, status);
        return true;
    }

    public bool TryAccept(
        RemoteInputCommand input,
        out AcceptedRemoteInput accepted)
    {
        accepted = default;
        RemoteSessionStatus? expired = null;
        lock (_gate)
        {
            var now = DateTimeOffset.UtcNow;
            if (!IsCurrentAlive(now))
            {
                if (_current is not null)
                {
                    expired = BuildStatus(
                        RemoteSessionState.Expired,
                        "Sesi remote kedaluwarsa");
                    Clear();
                }
            }
            else if (_current!.Mode == RemoteSessionMode.Control
                     && ContractValidation.IsValidRemoteInput(
                         input,
                         _pcName,
                         now.ToUnixTimeMilliseconds())
                     && string.Equals(
                         input.SessionId,
                         _current.SessionId,
                         StringComparison.Ordinal)
                     && input.SequenceNumber > _lastInputSequence)
            {
                _lastInputSequence = input.SequenceNumber;
                _acceptedInputCount++;
                accepted = new AcceptedRemoteInput(_current, input);
                return true;
            }
        }

        if (expired is not null) StatusChanged?.Invoke(this, expired);
        return false;
    }

    public bool EndLocal(
        RemoteSessionState state,
        string reason)
    {
        if (state is not (
                RemoteSessionState.Ended
                or RemoteSessionState.Expired
                or RemoteSessionState.EmergencyReleased))
        {
            throw new ArgumentOutOfRangeException(nameof(state));
        }

        RemoteSessionStatus? status;
        lock (_gate)
        {
            if (_current is null) return false;
            status = BuildStatus(state, reason);
            Clear();
        }

        StatusChanged?.Invoke(this, status);
        return true;
    }

    public bool ExpireIfNeeded()
    {
        lock (_gate)
        {
            if (_current is null
                || IsCurrentAlive(DateTimeOffset.UtcNow))
            {
                return false;
            }
        }

        return EndLocal(
            RemoteSessionState.Expired,
            "Sesi remote kedaluwarsa");
    }

    private bool IsCurrentAlive(DateTimeOffset now) =>
        _current is not null
        && _current.Active
        && _current.ExpiresAtUnixMs >= now.ToUnixTimeMilliseconds();

    private RemoteSessionStatus BuildStatus(
        RemoteSessionState state,
        string? message) =>
        RemoteSessionStatus.Create(
            _current!,
            state,
            _acceptedInputCount,
            message);

    private void Clear()
    {
        _current = null;
        _lastInputSequence = 0;
        _acceptedInputCount = 0;
    }
}
