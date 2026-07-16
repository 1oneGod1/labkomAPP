using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using LabKom.Shared.Discovery;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace LabKom.Student.Services;

/// <summary>Listens for signed Teacher beacons and ignores spoofed or stale packets.</summary>
public sealed class DiscoveryClient
{
    private readonly ILogger<DiscoveryClient> _logger;
    private readonly TeacherEndpointStore _store;
    private readonly string _sharedSecret;

    public DiscoveryClient(
        ILogger<DiscoveryClient> logger,
        TeacherEndpointStore store,
        IConfiguration configuration)
    {
        _logger = logger;
        _store = store;
        _sharedSecret = Environment.GetEnvironmentVariable("LABKOM_SHARED_SECRET")
                        ?? configuration["Agent:SharedSecret"]
                        ?? string.Empty;
    }

    public async Task ListenAsync(CancellationToken ct)
    {
        using var udp = new UdpClient();
        udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        udp.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryProtocol.Port));
        _logger.LogInformation("Agent discovery listener aktif di port {Port}", DiscoveryProtocol.Port);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var result = await udp.ReceiveAsync(ct);
                var json = Encoding.UTF8.GetString(result.Buffer);
                var beacon = JsonSerializer.Deserialize<DiscoveryBeacon>(json);
                if (beacon is null || !beacon.IsAuthentic(_sharedSecret))
                {
                    _logger.LogWarning("Beacon discovery tidak autentik dari {Endpoint}", result.RemoteEndPoint);
                    continue;
                }
                _store.Update(beacon);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (JsonException ex)
            {
                _logger.LogDebug(ex, "Beacon discovery berformat salah");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gagal memproses paket discovery");
                await Task.Delay(500, ct);
            }
        }
    }
}