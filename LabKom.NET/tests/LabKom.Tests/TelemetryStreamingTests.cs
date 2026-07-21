using LabKom.Shared.Contracts;
using LabKom.Shared.Devices;
using LabKom.Student.Services;
using LabKom.Teacher.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace LabKom.Tests;

public sealed class TelemetryStreamingTests
{
    [Fact]
    public void TelemetryContract_RejectsOutOfBoundsAndStaleSamples()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var valid = Sample("LAB-PC-01", 1, now);

        Assert.True(ContractValidation.IsValidDeviceTelemetry(
            valid,
            "LAB-PC-01",
            now));
        Assert.False(ContractValidation.IsValidDeviceTelemetry(
            valid with { CpuPercent = double.NaN },
            "LAB-PC-01",
            now));
        Assert.False(ContractValidation.IsValidDeviceTelemetry(
            valid with { UsedMemoryBytes = valid.TotalMemoryBytes + 1 },
            "LAB-PC-01",
            now));
        Assert.False(ContractValidation.IsValidDeviceTelemetry(
            valid with { TimestampUnixMs = now - 301_000 },
            "LAB-PC-01",
            now));
        Assert.False(ContractValidation.IsValidDeviceTelemetry(
            valid,
            "LAB-PC-02",
            now));
    }

    [Fact]
    public void TelemetryRegistry_RejectsDuplicateAndOutOfOrderSequence()
    {
        var now = DateTimeOffset.UtcNow;
        var registry = new TelemetryRegistry();

        Assert.True(registry.Push(Sample(
            "LAB-PC-01", 2, now.ToUnixTimeMilliseconds()), now));
        Assert.False(registry.Push(Sample(
            "LAB-PC-01", 2, now.ToUnixTimeMilliseconds()), now));
        Assert.False(registry.Push(Sample(
            "LAB-PC-01", 1, now.ToUnixTimeMilliseconds()), now));

        var summary = registry.Summary(now);
        Assert.Equal(1, summary.AcceptedSamples);
        Assert.Equal(2, summary.RejectedSamples);
        Assert.Equal(1, summary.ActiveDevices);
    }

    [Fact]
    public void TelemetryRegistry_HandlesFortyConcurrentStreams()
    {
        const int clients = 40;
        const int samplesPerClient = 250;
        var now = DateTimeOffset.UtcNow;
        var registry = new TelemetryRegistry();
        var events = 0;
        registry.TelemetryReceived += (_, _) => Interlocked.Increment(ref events);

        Parallel.For(1, clients + 1, client =>
        {
            var pcName = $"LOAD-PC-{client:00}";
            for (var sequence = 1; sequence <= samplesPerClient; sequence++)
            {
                Assert.True(registry.Push(
                    Sample(pcName, sequence, now.ToUnixTimeMilliseconds()),
                    now));
            }
        });

        var summary = registry.Summary(now);
        Assert.Equal(clients, summary.ActiveDevices);
        Assert.Equal(clients * samplesPerClient, summary.AcceptedSamples);
        Assert.Equal(0, summary.RejectedSamples);
        Assert.Equal(clients * samplesPerClient, events);
        Assert.InRange(summary.P95LatencyMs, 0, 1);
    }

    [Fact]
    public async Task TelemetryRecorder_WritesValidatedCsvSession()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "labkom-telemetry-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Telemetry:OutputDirectory"] = directory,
                })
                .Build();
            var registry = new TelemetryRegistry();
            var recorder = new TelemetrySessionRecorder(
                registry,
                configuration,
                NullLogger<TelemetrySessionRecorder>.Instance);
            await recorder.StartAsync(CancellationToken.None);
            var now = DateTimeOffset.UtcNow;
            for (var client = 1; client <= 40; client++)
            {
                registry.Push(
                    Sample(
                        $"LOAD-PC-{client:00}",
                        1,
                        now.ToUnixTimeMilliseconds()),
                    now);
            }
            await recorder.StopAsync(CancellationToken.None);

            Assert.Equal(40, recorder.RecordedSamples);
            Assert.Equal(0, recorder.DroppedSamples);
            Assert.True(File.Exists(recorder.SessionFilePath));
            Assert.Equal(41, File.ReadLines(recorder.SessionFilePath).Count());
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void TelemetryCollector_ProducesValidWindowsSample()
    {
        var collector = new DeviceTelemetryCollector(new MachineIdentity());
        var sample = collector.Capture();

        Assert.True(ContractValidation.IsValidDeviceTelemetry(
            sample,
            Environment.MachineName,
            sample.TimestampUnixMs));
    }

    private static DeviceTelemetry Sample(
        string pcName,
        long sequence,
        long timestampUnixMs) =>
        new(
            pcName,
            sequence,
            timestampUnixMs,
            UptimeSeconds: 3_600,
            CpuPercent: 35,
            UsedMemoryBytes: 8L * 1024 * 1024 * 1024,
            TotalMemoryBytes: 16L * 1024 * 1024 * 1024,
            DiskFreeBytes: 200L * 1024 * 1024 * 1024,
            DiskTotalBytes: 500L * 1024 * 1024 * 1024,
            NetworkReceiveBytesPerSecond: 2L * 1024 * 1024,
            NetworkSendBytesPerSecond: 512L * 1024,
            AgentWorkingSetBytes: 128L * 1024 * 1024,
            AgentThreadCount: 24);
}
