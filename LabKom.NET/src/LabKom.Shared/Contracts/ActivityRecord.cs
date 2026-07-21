namespace LabKom.Shared.Contracts;

public enum ActivityRecordKind
{
    WindowChange = 1,
    ProcessStart = 2,
    ProcessStop = 3,
    Idle = 4,
    Resume = 5,
    System = 6,
    UsageSample = 7,
    FileCollected = 8,
    Registration = 9,
    Assessment = 10,
}

public enum ActivityCategory
{
    Desktop = 1,
    Application = 2,
    WebBrowser = 3,
    System = 4,
}

/// <summary>
/// Ringkasan aktivitas tanpa isi ketikan. KeyboardEventCount hanya jumlah
/// key-down sejak sampel sebelumnya dan tidak pernah berisi tombol/teks.
/// </summary>
public sealed record ActivityMetrics(
    ActivityCategory Category,
    int KeyboardEventCount,
    long IdleMilliseconds);

/// <summary>
/// Catatan aktivitas yang dikirim Student → Teacher untuk dimasukkan
/// ke activity feed UI dan dipersist ke SQLite.
/// </summary>
public sealed record ActivityRecord(
    string PcName,
    ActivityRecordKind Kind,
    string Title,
    string? ProcessName,
    long TimestampUnixMs,
    ActivityMetrics? Metrics = null)
{
    public static ActivityRecord WindowChange(string pcName, string windowTitle, string? processName) => new(
        pcName,
        ActivityRecordKind.WindowChange,
        windowTitle,
        processName,
        DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

    public static ActivityRecord Usage(
        string pcName,
        string windowTitle,
        string? processName,
        ActivityCategory category,
        int keyboardEventCount,
        long idleMilliseconds) => new(
            pcName,
            ActivityRecordKind.UsageSample,
            windowTitle,
            processName,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            new ActivityMetrics(category, keyboardEventCount, idleMilliseconds));
}
