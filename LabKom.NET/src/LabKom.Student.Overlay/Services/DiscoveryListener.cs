using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using LabKom.Shared.Discovery;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LabKom.Student.Overlay.Services;

public class DiscoveryListener : BackgroundService
{
    private readonly TeacherEndpointStore _store;
    private readonly ILogger<DiscoveryListener> _logger;

    public DiscoveryListener(TeacherEndpointStore store, ILogger<DiscoveryListener> logger)
    {
        _store = store;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var udp = new UdpClient();
        udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        udp.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryProtocol.Port));
        _logger.LogInformation("Overlay discovery listener aktif di port {Port}", DiscoveryProtocol.Port);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = await udp.ReceiveAsync(stoppingToken);
                var json = Encoding.UTF8.GetString(result.Buffer);
                var beacon = JsonSerializer.Deserialize<DiscoveryBeacon>(json);
                if (beacon is null || !beacon.IsValid()) continue;
                _store.Update(beacon);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Discovery listener error");
                await Task.Delay(500, stoppingToken);
            }
        }
    }
}
