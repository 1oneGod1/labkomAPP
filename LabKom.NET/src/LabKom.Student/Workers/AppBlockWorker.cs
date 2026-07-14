using LabKom.Student.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LabKom.Student.Workers;

/// <summary>
/// Loop scan tiap 3 detik untuk menegakkan AppBlockPolicy.
/// </summary>
public class AppBlockWorker : BackgroundService
{
    private readonly AppBlockEnforcer _enforcer;
    private readonly ILogger<AppBlockWorker> _logger;

    public AppBlockWorker(AppBlockEnforcer enforcer, ILogger<AppBlockWorker> logger)
    {
        _enforcer = enforcer;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("AppBlockWorker dimulai");
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _enforcer.ScanAndKill();
                await Task.Delay(3000, stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "AppBlockWorker error");
                await Task.Delay(3000, stoppingToken);
            }
        }
    }
}
