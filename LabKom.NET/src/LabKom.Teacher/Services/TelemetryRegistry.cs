using System.Collections.Concurrent;
using LabKom.Shared.Contracts;

namespace LabKom.Teacher.Services;

public sealed class TelemetryRegistry
{
    private const int MaximumLatencySamples = 4_096;
    private static readonly TimeSpan ActiveWindow = TimeSpan.FromSeconds(10);
    private readonly ConcurrentDictionary<string, DeviceTelemetrySnapshot> _latest =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentQueue<double> _latencies = new();
    private long _accepted;
    private long _rejected;

    public event EventHandler<DeviceTelemetryUpdate>? TelemetryReceived;

    public IReadOnlyCollection<DeviceTelemetrySnapshot> Snapshot() =>
        _latest.Values.ToArray();

    public DeviceTelemetrySnapshot? Get(string pcName) =>
        _latest.TryGetValue(pcName, out var snapshot) ? snapshot : null;

    public bool Push(DeviceTelemetry telemetry, DateTimeOffset? receivedAt = null)
    {
        var received = receivedAt ?? DateTimeOffset.UtcNow;
        var latencyMs = Math.Max(
            0,
            received.ToUnixTimeMilliseconds() - telemetry.TimestampUnixMs);
        var snapshot = new DeviceTelemetrySnapshot(
            telemetry,
            received,
            latencyMs,
            EvaluateHealth(telemetry, latencyMs));

        while (true)
        {
            if (!_latest.TryGetValue(telemetry.PcName, out var current))
            {
                if (_latest.TryAdd(telemetry.PcName, snapshot)) break;
                continue;
            }
            if (telemetry.SequenceNumber <= current.Telemetry.SequenceNumber)
            {
                Reject();
                return false;
            }
            if (_latest.TryUpdate(telemetry.PcName, snapshot, current)) break;
        }

        Interlocked.Increment(ref _accepted);
        _latencies.Enqueue(latencyMs);
        while (_latencies.Count > MaximumLatencySamples)
            _latencies.TryDequeue(out _);
        TelemetryReceived?.Invoke(
            this,
            new DeviceTelemetryUpdate(snapshot, Summary(received)));
        return true;
    }

    public void Reject() => Interlocked.Increment(ref _rejected);

    public DeviceTelemetrySummary Summary(DateTimeOffset? now = null)
    {
        var currentTime = now ?? DateTimeOffset.UtcNow;
        var snapshots = _latest.Values.ToArray();
        var active = snapshots.Where(snapshot =>
                currentTime - snapshot.ReceivedAtUtc <= ActiveWindow)
            .ToArray();
        var warning = active.Count(snapshot =>
            snapshot.Health is TelemetryHealth.Warning or TelemetryHealth.Critical);
        var critical = active.Count(snapshot =>
            snapshot.Health == TelemetryHealth.Critical);
        var latencies = _latencies.ToArray();
        Array.Sort(latencies);
        var p95 = latencies.Length == 0
            ? 0
            : latencies[(int)Math.Ceiling(latencies.Length * 0.95) - 1];
        return new DeviceTelemetrySummary(
            snapshots.Length,
            active.Length,
            snapshots.Length - active.Length,
            warning,
            critical,
            Interlocked.Read(ref _accepted),
            Interlocked.Read(ref _rejected),
            p95);
    }

    private static TelemetryHealth EvaluateHealth(
        DeviceTelemetry telemetry,
        double latencyMs)
    {
        var memoryPercent = Percent(
            telemetry.UsedMemoryBytes,
            telemetry.TotalMemoryBytes);
        var diskFreePercent = Percent(
            telemetry.DiskFreeBytes,
            telemetry.DiskTotalBytes);
        if (telemetry.CpuPercent >= 95
            || memoryPercent >= 95
            || diskFreePercent <= 5
            || latencyMs >= 5_000)
            return TelemetryHealth.Critical;
        if (telemetry.CpuPercent >= 85
            || memoryPercent >= 85
            || diskFreePercent <= 10
            || latencyMs >= 1_500)
            return TelemetryHealth.Warning;
        return TelemetryHealth.Healthy;
    }

    private static double Percent(long value, long total) =>
        total <= 0 ? 0 : 100d * value / total;
}

public enum TelemetryHealth
{
    Healthy = 1,
    Warning = 2,
    Critical = 3,
}

public sealed record DeviceTelemetrySnapshot(
    DeviceTelemetry Telemetry,
    DateTimeOffset ReceivedAtUtc,
    double LatencyMs,
    TelemetryHealth Health);

public sealed record DeviceTelemetrySummary(
    int KnownDevices,
    int ActiveDevices,
    int StaleDevices,
    int WarningDevices,
    int CriticalDevices,
    long AcceptedSamples,
    long RejectedSamples,
    double P95LatencyMs);

public sealed record DeviceTelemetryUpdate(
    DeviceTelemetrySnapshot Snapshot,
    DeviceTelemetrySummary Summary);
