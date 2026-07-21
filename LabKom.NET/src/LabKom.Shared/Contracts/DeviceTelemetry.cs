namespace LabKom.Shared.Contracts;

/// <summary>
/// Bounded, ordered health sample streamed by Student Agent. Values describe
/// the student machine, except AgentWorkingSetBytes and AgentThreadCount.
/// </summary>
public sealed record DeviceTelemetry(
    string PcName,
    long SequenceNumber,
    long TimestampUnixMs,
    long UptimeSeconds,
    double CpuPercent,
    long UsedMemoryBytes,
    long TotalMemoryBytes,
    long DiskFreeBytes,
    long DiskTotalBytes,
    long NetworkReceiveBytesPerSecond,
    long NetworkSendBytesPerSecond,
    long AgentWorkingSetBytes,
    int AgentThreadCount);
