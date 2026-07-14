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
    private readonly ILogger<AgentWorker> _logger;
    private readonly TimeSpan _heartbeat;
    private readonly TimeSpan _reconnect;

    public AgentWorker(
        HubConnectionService hub,
        TeacherEndpointStore endpointStore,
        IConfiguration config,
        ILogger<AgentWorker> logger)
    {
        _hub = hub;
        _endpointStore = endpointStore;
        _logger = logger;
        _heartbeat = TimeSpan.FromSeconds(config.GetValue("Agent:HeartbeatIntervalSeconds", 5));
        _reconnect = TimeSpan.FromSeconds(config.GetValue("Agent:ReconnectDelaySeconds", 3));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("AgentWorker dimulai");
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!_endpointStore.IsFresh)
                {
                    await Task.Delay(_reconnect, stoppingToken);
                    continue;
                }

                if (!_hub.IsConnected)
                {
                    await _hub.EnsureConnectedAsync(stoppingToken);
                    await Task.Delay(_reconnect, stoppingToken);
                    continue;
                }

                await _hub.SendHeartbeatAsync(stoppingToken);
                await Task.Delay(_heartbeat, stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Loop agent error");
                await Task.Delay(_reconnect, stoppingToken);
            }
        }
    }
}
