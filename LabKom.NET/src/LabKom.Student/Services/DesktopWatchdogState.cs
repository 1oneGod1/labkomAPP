namespace LabKom.Student.Services;

/// <summary>Pure restart throttling policy used by the Student Desktop watchdog.</summary>
public sealed class DesktopWatchdogState
{
    private DateTimeOffset? _missingSinceUtc;
    private DateTimeOffset? _lastStartAttemptUtc;

    public bool ShouldAttemptStart(
        bool hasInteractiveSession,
        bool desktopRunning,
        DateTimeOffset now,
        TimeSpan missingGrace,
        TimeSpan restartCooldown)
    {
        if (!hasInteractiveSession || desktopRunning)
        {
            _missingSinceUtc = null;
            return false;
        }

        _missingSinceUtc ??= now;
        if (now - _missingSinceUtc < missingGrace) return false;
        if (_lastStartAttemptUtc is { } last
            && now - last < restartCooldown) return false;

        _lastStartAttemptUtc = now;
        return true;
    }
}
