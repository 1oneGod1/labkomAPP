using LabKom.Student.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LabKom.Student.Workers;

public class DiscoveryWorker : BackgroundService
{
    private readonly DiscoveryClient _client;
    private readonly ILogger<DiscoveryWorker> _logger;

    public DiscoveryWorker(DiscoveryClient client, ILogger<DiscoveryWorker> logger)
    {
        _client = client;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("DiscoveryWorker dimulai");
        await _client.ListenAsync(stoppingToken);
    }
}
