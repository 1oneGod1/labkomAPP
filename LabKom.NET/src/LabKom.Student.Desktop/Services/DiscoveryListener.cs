using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using LabKom.Shared.Discovery;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LabKom.Student.Desktop.Services;

/// <summary>Interactive-session listener for authenticated Teacher discovery.</summary>
public sealed class DiscoveryListener : BackgroundService
{
    private readonly TeacherEndpointStore _store;
    private readonly ILogger<DiscoveryListener> _logger;
    private readonly string _sharedSecret;

    public DiscoveryListener(
        TeacherEndpointStore store,
        ILogger<DiscoveryListener> logger,
        IConfiguration configuration)
    {
        _store = store;
        _logger = logger;
        _sharedSecret = Environment.GetEnvironmentVariable("LABKOM_SHARED_SECRET")
                        ?? configuration["Desktop:SharedSecret"]
                        ?? string.Empty;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var udp = new UdpClient();
        udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        udp.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryProtocol.Port));
        _logger.LogInformation("Desktop discovery listener aktif di port {Port}", DiscoveryProtocol.Port);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = await udp.ReceiveAsync(stoppingToken);
                var json = Encoding.UTF8.GetString(result.Buffer);
                var beacon = JsonSerializer.Deserialize<DiscoveryBeacon>(json);
                if (beacon is null || !beacon.IsAuthentic(_sharedSecret))
                {
                    _logger.LogWarning("Beacon discovery tidak autentik dari {Endpoint}", result.RemoteEndPoint);
                    continue;
                }
                _store.Update(beacon);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (JsonException ex)
            {
                _logger.LogDebug(ex, "Beacon discovery berformat salah");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Desktop discovery listener gagal");
                await Task.Delay(500, stoppingToken);
            }
        }
    }
}