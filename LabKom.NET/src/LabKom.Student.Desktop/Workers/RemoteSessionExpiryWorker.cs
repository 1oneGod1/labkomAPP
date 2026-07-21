using Microsoft.Extensions.Hosting;

namespace LabKom.Student.Desktop.Workers;

public sealed class RemoteSessionExpiryWorker : BackgroundService
{
    private readonly Services.RemoteSessionController _sessions;

    public RemoteSessionExpiryWorker(Services.RemoteSessionController sessions)
    {
        _sessions = sessions;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            _sessions.ExpireIfNeeded();
        }
    }
}
