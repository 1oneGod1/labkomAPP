namespace LabKom.Shared.Contracts;

public enum PowerAction
{
    Shutdown = 1,
    Restart = 2,
    LogOff = 3,
}

/// <summary>
/// Perintah power dari Teacher → Student. Eksekusi via shutdown.exe.
/// </summary>
public sealed record PowerCommand(
    PowerAction Action,
    int DelaySeconds,
    string? Reason)
{
    public static PowerCommand Shutdown(int delaySec = 0, string? reason = null) =>
        new(PowerAction.Shutdown, delaySec, reason);

    public static PowerCommand Restart(int delaySec = 0, string? reason = null) =>
        new(PowerAction.Restart, delaySec, reason);
}
