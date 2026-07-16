using LabKom.Shared.Discovery;
using LabKom.Student.Desktop.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LabKom.Student.Desktop.Workers;

public class DesktopConnectionWorker : BackgroundService
{
    private readonly DesktopHubClient _hub;
    private readonly TeacherEndpointStore _endpoints;
    private readonly ILogger<DesktopConnectionWorker> _logger;
    private readonly TimeSpan _delay;

    public DesktopConnectionWorker(
        DesktopHubClient hub,
        TeacherEndpointStore endpoints,
        IConfiguration config,
        ILogger<DesktopConnectionWorker> logger)
    {
        _hub = hub;
        _endpoints = endpoints;
        _logger = logger;
        _delay = TimeSpan.FromSeconds(config.GetValue("Desktop:ReconnectDelaySeconds", 3));
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
                _logger.LogWarning(ex, "Desktop connection loop error");
                await Task.Delay(_delay, stoppingToken);
            }
        }
    }
}
