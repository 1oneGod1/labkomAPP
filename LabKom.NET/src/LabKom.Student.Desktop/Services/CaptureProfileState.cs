using LabKom.Shared.Contracts;

namespace LabKom.Student.Desktop.Services;

public readonly record struct CaptureSelection(CaptureProfile Profile, string? MonitorId);

/// <summary>Thread-safe capture selection updated by Teacher commands.</summary>
public sealed class CaptureProfileState
{
    private readonly object _sync = new();
    private CaptureSelection _current = new(CaptureProfile.Thumbnail, null);

    public CaptureSelection Current
    {
        get
        {
            lock (_sync) return _current;
        }
    }

    public bool TryApply(CaptureProfileCommand command)
    {
        if (!ContractValidation.IsValidCaptureProfileCommand(command)) return false;

        lock (_sync)
        {
            _current = new CaptureSelection(command.Profile, command.MonitorId?.Trim());
        }

        return true;
    }
}
