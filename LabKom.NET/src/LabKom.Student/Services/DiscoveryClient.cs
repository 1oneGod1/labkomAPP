using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using LabKom.Shared.Discovery;
using Microsoft.Extensions.Logging;

namespace LabKom.Student.Services;

/// <summary>
/// Mendengarkan UDP broadcast dari Teacher Console di port DiscoveryProtocol.Port.
/// Setiap beacon yang valid diteruskan ke TeacherEndpointStore.
/// </summary>
public class DiscoveryClient
{
    private readonly ILogger<DiscoveryClient> _logger;
    private readonly TeacherEndpointStore _store;

    public DiscoveryClient(ILogger<DiscoveryClient> logger, TeacherEndpointStore store)
    {
        _logger = logger;
        _store = store;
    }

    public async Task ListenAsync(CancellationToken ct)
    {
        using var udp = new UdpClient();
        udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        udp.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryProtocol.Port));
        _logger.LogInformation("Discovery listener aktif di port {Port}", DiscoveryProtocol.Port);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var result = await udp.ReceiveAsync(ct);
                var json = Encoding.UTF8.GetString(result.Buffer);
                var beacon = JsonSerializer.Deserialize<DiscoveryBeacon>(json);
                if (beacon is null || !beacon.IsValid())
                {
                    _logger.LogDebug("Beacon tidak valid dari {Endpoint}", result.RemoteEndPoint);
                    continue;
                }
                _store.Update(beacon);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gagal memproses paket discovery");
                await Task.Delay(500, ct);
            }
        }
    }
}
