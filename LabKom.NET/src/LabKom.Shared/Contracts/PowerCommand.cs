namespace LabKom.Shared.Contracts;

public enum PowerAction
{
    Shutdown = 1,
    Restart = 2,
    LogOff = 3,
}

/// <summary>Perintah power privileged dengan identity dan TTL.</summary>
public sealed record PowerCommand(
    string CommandId,
    PowerAction Action,
    int DelaySeconds,
    string? Reason,
    long IssuedAtUnixMs,
    long ExpiresAtUnixMs)
{
    public static PowerCommand Shutdown(int delaySec = 0, string? reason = null) =>
        Create(PowerAction.Shutdown, delaySec, reason);

    public static PowerCommand Restart(int delaySec = 0, string? reason = null) =>
        Create(PowerAction.Restart, delaySec, reason);

    public static PowerCommand LogOff(string? reason = null) =>
        Create(PowerAction.LogOff, 0, reason);

    private static PowerCommand Create(PowerAction action, int delaySec, string? reason)
    {
        var issued = DateTimeOffset.UtcNow;
        return new PowerCommand(
            Guid.NewGuid().ToString("N"),
            action,
            delaySec,
            reason,
            issued.ToUnixTimeMilliseconds(),
            issued.AddSeconds(30).ToUnixTimeMilliseconds());
    }
}
