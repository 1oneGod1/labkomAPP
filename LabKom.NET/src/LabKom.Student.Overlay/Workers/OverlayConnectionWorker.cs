using LabKom.Student.Overlay.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LabKom.Student.Overlay.Workers;

public class OverlayConnectionWorker : BackgroundService
{
    private readonly OverlayHubClient _hub;
    private readonly TeacherEndpointStore _endpoints;
    private readonly ILogger<OverlayConnectionWorker> _logger;
    private readonly TimeSpan _delay;

    public OverlayConnectionWorker(
        OverlayHubClient hub,
        TeacherEndpointStore endpoints,
        IConfiguration config,
        ILogger<OverlayConnectionWorker> logger)
    {
        _hub = hub;
        _endpoints = endpoints;
        _logger = logger;
        _delay = TimeSpan.FromSeconds(config.GetValue("Overlay:ReconnectDelaySeconds", 3));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (_endpoints.IsFresh && !_hub.IsConnected)
                {
                    await _hub.EnsureConnectedAsync(stoppingToken);
                }
                await Task.Delay(_delay, stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Overlay connection loop error");
                await Task.Delay(_delay, stoppingToken);
            }
        }
    }
}
