using System.IO;
using System.Globalization;
using System.Text;
using System.Threading.Channels;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LabKom.Teacher.Services;

/// <summary>Continuously records validated telemetry as a pilot-friendly CSV.</summary>
public sealed class TelemetrySessionRecorder : IHostedService
{
    private const int QueueCapacity = 20_000;
    private readonly TelemetryRegistry _registry;
    private readonly IConfiguration _configuration;
    private readonly ILogger<TelemetrySessionRecorder> _logger;
    private readonly Channel<DeviceTelemetrySnapshot> _queue =
        Channel.CreateBounded<DeviceTelemetrySnapshot>(
            new BoundedChannelOptions(QueueCapacity)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait,
            });
    private Task _consumer = Task.CompletedTask;
    private long _recorded;
    private long _dropped;

    public TelemetrySessionRecorder(
        TelemetryRegistry registry,
        IConfiguration configuration,
        ILogger<TelemetrySessionRecorder> logger)
    {
        _registry = registry;
        _configuration = configuration;
        _logger = logger;
    }

    public string SessionFilePath { get; private set; } = string.Empty;
    public long RecordedSamples => Interlocked.Read(ref _recorded);
    public long DroppedSamples => Interlocked.Read(ref _dropped);

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var configured = _configuration["Telemetry:OutputDirectory"]
                         ?? "%LOCALAPPDATA%\\LabKom\\Telemetry";
        var directory = Path.GetFullPath(
            Environment.ExpandEnvironmentVariables(configured));
        Directory.CreateDirectory(directory);
        SessionFilePath = Path.Combine(
            directory,
            $"telemetry-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}.csv");
        _registry.TelemetryReceived += OnTelemetryReceived;
        _consumer = ConsumeAsync();
        _logger.LogInformation(
            "Perekaman telemetry dimulai: {Path}",
            SessionFilePath);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _registry.TelemetryReceived -= OnTelemetryReceived;
        _queue.Writer.TryComplete();
        try
        {
            await _consumer.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Perekaman telemetry berhenti sebelum queue habis");
        }
    }

    private void OnTelemetryReceived(object? sender, DeviceTelemetryUpdate update)
    {
        if (_queue.Writer.TryWrite(update.Snapshot)) return;
        var dropped = Interlocked.Increment(ref _dropped);
        if (dropped == 1 || dropped % 100 == 0)
            _logger.LogError("Queue telemetry penuh; {Count} sampel dibuang", dropped);
    }

    private async Task ConsumeAsync()
    {
        await using var stream = new FileStream(
            SessionFilePath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.Read,
            65_536,
            useAsync: true);
        await using var writer = new StreamWriter(
            stream,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        await writer.WriteLineAsync(
            "receivedUtc,sampleUtc,pcName,sequence,health,latencyMs,uptimeSeconds," +
            "cpuPercent,memoryUsedBytes,memoryTotalBytes,diskFreeBytes,diskTotalBytes," +
            "networkReceiveBps,networkSendBps,agentWorkingSetBytes,agentThreadCount");

        var pendingFlush = 0;
        await foreach (var snapshot in _queue.Reader.ReadAllAsync())
        {
            await writer.WriteLineAsync(ToCsv(snapshot));
            Interlocked.Increment(ref _recorded);
            if (++pendingFlush < 10) continue;
            await writer.FlushAsync();
            pendingFlush = 0;
        }
        await writer.FlushAsync();
    }

    private static string ToCsv(DeviceTelemetrySnapshot snapshot)
    {
        var telemetry = snapshot.Telemetry;
        return string.Join(',', new[]
        {
            snapshot.ReceivedAtUtc.ToString("O", CultureInfo.InvariantCulture),
            DateTimeOffset.FromUnixTimeMilliseconds(telemetry.TimestampUnixMs)
                .ToString("O", CultureInfo.InvariantCulture),
            Escape(telemetry.PcName),
            telemetry.SequenceNumber.ToString(CultureInfo.InvariantCulture),
            snapshot.Health.ToString(),
            snapshot.LatencyMs.ToString("F1", CultureInfo.InvariantCulture),
            telemetry.UptimeSeconds.ToString(CultureInfo.InvariantCulture),
            telemetry.CpuPercent.ToString("F1", CultureInfo.InvariantCulture),
            telemetry.UsedMemoryBytes.ToString(CultureInfo.InvariantCulture),
            telemetry.TotalMemoryBytes.ToString(CultureInfo.InvariantCulture),
            telemetry.DiskFreeBytes.ToString(CultureInfo.InvariantCulture),
            telemetry.DiskTotalBytes.ToString(CultureInfo.InvariantCulture),
            telemetry.NetworkReceiveBytesPerSecond.ToString(CultureInfo.InvariantCulture),
            telemetry.NetworkSendBytesPerSecond.ToString(CultureInfo.InvariantCulture),
            telemetry.AgentWorkingSetBytes.ToString(CultureInfo.InvariantCulture),
            telemetry.AgentThreadCount.ToString(CultureInfo.InvariantCulture),
        });
    }

    private static string Escape(string value) =>
        value.IndexOfAny([',', '"', '\r', '\n']) < 0
            ? value
            : $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
}
