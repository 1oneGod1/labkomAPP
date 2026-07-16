using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using LabKom.Shared.Discovery;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LabKom.Teacher.Services;

/// <summary>
/// Broadcast UDP berkala ke 255.255.255.255 dari semua NIC aktif,
/// supaya Student Agent di subnet yang sama bisa menemukan Teacher Console.
/// </summary>
public class DiscoveryBroadcaster : BackgroundService
{
    private readonly ILogger<DiscoveryBroadcaster> _logger;
    private readonly IConfiguration _config;
    private readonly TeacherCertificateProvider _certificate;
    private readonly string _teacherId = Environment.MachineName + "-" + Guid.NewGuid().ToString("N")[..8];

    public DiscoveryBroadcaster(ILogger<DiscoveryBroadcaster> logger, IConfiguration config, TeacherCertificateProvider certificate)
    {
        _logger = logger;
        _config = config;
        _certificate = certificate;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(_config.GetValue("Teacher:DiscoveryBroadcastIntervalSeconds", 2));
        var hubPort = _config.GetValue("Teacher:HubPort", 41235);
        var teacherName = _config.GetValue("Teacher:Name", "LabKom Teacher")!;
        var sharedSecret = Environment.GetEnvironmentVariable("LABKOM_SHARED_SECRET")
                           ?? _config["Teacher:SharedSecret"]
                           ?? string.Empty;
        if (!LabKom.Shared.Hub.HubSecurity.IsStrongSecret(sharedSecret))
        {
            _logger.LogError("Discovery tidak dijalankan: shared secret wajib minimal {Length} karakter.", LabKom.Shared.Hub.HubSecurity.MinimumSecretLength);
            return;
        }

        using var udp = new UdpClient { EnableBroadcast = true };
        var endpoint = new IPEndPoint(IPAddress.Broadcast, DiscoveryProtocol.Port);

        _logger.LogInformation("Discovery broadcaster aktif: tiap {Interval}s ke port {Port}", interval, DiscoveryProtocol.Port);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                foreach (var ip in GetLanIpAddresses())
                {
                    var beacon = DiscoveryBeacon.CreateSigned(_teacherId, teacherName, ip, hubPort, _certificate.Sha256Pin, sharedSecret);
                    var json = JsonSerializer.Serialize(beacon);
                    var bytes = Encoding.UTF8.GetBytes(json);
                    await udp.SendAsync(bytes, bytes.Length, endpoint);
                }
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Broadcast gagal");
                await Task.Delay(interval, stoppingToken);
            }
        }
    }

    private static IEnumerable<string> GetLanIpAddresses()
    {
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces()
                     .Where(n => n.OperationalStatus == OperationalStatus.Up
                                 && n.NetworkInterfaceType != NetworkInterfaceType.Loopback))
        {
            foreach (var addr in nic.GetIPProperties().UnicastAddresses
                         .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork
                                     && !IPAddress.IsLoopback(a.Address)))
            {
                yield return addr.Address.ToString();
            }
        }
    }
}
