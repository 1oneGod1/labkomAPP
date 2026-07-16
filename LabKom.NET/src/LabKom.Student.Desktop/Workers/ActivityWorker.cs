using LabKom.Shared.Contracts;
using LabKom.Shared.Devices;
using LabKom.Student.Desktop.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LabKom.Student.Desktop.Workers;

/// <summary>
/// Loop tiap 2 detik: snapshot active window, kirim ke Hub jika berubah
/// dari snapshot sebelumnya. Hemat bandwidth & noise di activity feed.
/// </summary>
public class ActivityWorker : BackgroundService
{
    private readonly ActivityMonitor _monitor;
    private readonly DesktopHubClient _hub;
    private readonly MachineIdentity _identity;
    private readonly ILogger<ActivityWorker> _logger;
    private ActivitySnapshot? _last;

    public ActivityWorker(
        ActivityMonitor monitor,
        DesktopHubClient hub,
        MachineIdentity identity,
        ILogger<ActivityWorker> logger)
    {
        _monitor = monitor;
        _hub = hub;
        _identity = identity;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ActivityWorker dimulai");
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!_hub.IsConnected) { await Task.Delay(2000, stoppingToken); continue; }

                var snap = _monitor.Snapshot();
                if (snap is not null && !Equals(snap, _last))
                {
                    var record = ActivityRecord.WindowChange(_identity.PcName, snap.WindowTitle, snap.ProcessName);
                    await _hub.PushActivityAsync(record, stoppingToken);
                    _last = snap;
                }
                await Task.Delay(2000, stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "ActivityWorker error");
                await Task.Delay(2000, stoppingToken);
            }
        }
    }
}
