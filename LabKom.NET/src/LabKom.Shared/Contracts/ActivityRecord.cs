namespace LabKom.Shared.Contracts;

public enum ActivityRecordKind
{
    WindowChange = 1,
    ProcessStart = 2,
    ProcessStop = 3,
    Idle = 4,
    Resume = 5,
    System = 6,
}

/// <summary>
/// Catatan aktivitas yang dikirim Student → Teacher untuk dimasukkan
/// ke activity feed UI dan dipersist ke SQLite.
/// </summary>
public sealed record ActivityRecord(
    string PcName,
    ActivityRecordKind Kind,
    string Title,
    string? ProcessName,
    long TimestampUnixMs)
{
    public static ActivityRecord WindowChange(string pcName, string windowTitle, string? processName) => new(
        pcName,
        ActivityRecordKind.WindowChange,
        windowTitle,
        processName,
        DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
}
