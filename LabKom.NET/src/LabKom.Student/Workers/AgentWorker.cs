using LabKom.Shared.Discovery;
using LabKom.Student.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LabKom.Student.Workers;

/// <summary>
/// Loop utama agent: pastikan koneksi ke Teacher Hub hidup, kirim heartbeat berkala.
/// Reconnection otomatis ditangani oleh HubConnectionService + TeacherEndpointStore.
/// </summary>
public class AgentWorker : BackgroundService
{
    private readonly HubConnectionService _hub;
    private readonly TeacherEndpointStore _endpointStore;
    private readonly DeviceTelemetryCollector _telemetryCollector;
    private readonly ILogger<AgentWorker> _logger;
    private readonly TimeSpan _heartbeat;
    private readonly TimeSpan _telemetry;
    private readonly TimeSpan _reconnect;

    public AgentWorker(
        HubConnectionService hub,
        TeacherEndpointStore endpointStore,
        DeviceTelemetryCollector telemetryCollector,
        IConfiguration config,
        ILogger<AgentWorker> logger)
    {
        _hub = hub;
        _endpointStore = endpointStore;
        _telemetryCollector = telemetryCollector;
        _logger = logger;
        _heartbeat = TimeSpan.FromSeconds(Math.Clamp(
            config.GetValue("Agent:HeartbeatIntervalSeconds", 5),
            2,
            60));
        _telemetry = TimeSpan.FromSeconds(Math.Clamp(
            config.GetValue("Telemetry:IntervalSeconds", 2),
            1,
            60));
        _reconnect = TimeSpan.FromSeconds(Math.Clamp(
            config.GetValue("Agent:ReconnectDelaySeconds", 3),
            1,
            60));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "AgentWorker dimulai; heartbeat={Heartbeat}s telemetry={Telemetry}s",
            _heartbeat.TotalSeconds,
            _telemetry.TotalSeconds);
        var nextHeartbeat = DateTimeOffset.MinValue;
        var nextTelemetry = DateTimeOffset.MinValue;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!_endpointStore.IsFresh)
                {
                    nextHeartbeat = nextTelemetry = DateTimeOffset.MinValue;
                    await Task.Delay(_reconnect, stoppingToken);
                    continue;
                }

                if (!_hub.IsConnected)
                {
                    await _hub.EnsureConnectedAsync(stoppingToken);
                    if (!_hub.IsConnected)
                    {
                        await Task.Delay(_reconnect, stoppingToken);
                        continue;
                    }
                    nextHeartbeat = nextTelemetry = DateTimeOffset.UtcNow;
                }

                var now = DateTimeOffset.UtcNow;
                if (now >= nextHeartbeat)
                {
                    await _hub.SendHeartbeatAsync(stoppingToken);
                    nextHeartbeat = now + _heartbeat;
                }
                if (now >= nextTelemetry)
                {
                    await _hub.SendTelemetryAsync(
                        _telemetryCollector.Capture(),
                        stoppingToken);
                    nextTelemetry = now + _telemetry;
                }

                var next = nextHeartbeat <= nextTelemetry
                    ? nextHeartbeat
                    : nextTelemetry;
                var delay = next - DateTimeOffset.UtcNow;
                await Task.Delay(
                    delay < TimeSpan.FromMilliseconds(250)
                        ? TimeSpan.FromMilliseconds(250)
                        : delay,
                    stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Loop agent error");
                await Task.Delay(_reconnect, stoppingToken);
            }
        }
    }
}
