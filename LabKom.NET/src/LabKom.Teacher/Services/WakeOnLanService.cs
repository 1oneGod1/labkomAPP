using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;

namespace LabKom.Teacher.Services;

/// <summary>
/// Mengirim magic packet (102 byte: 6 byte 0xFF + MAC×16) ke broadcast LAN
/// supaya PC siswa dengan WoL aktif bisa boot dari kondisi shutdown.
/// </summary>
public class WakeOnLanService
{
    private readonly ILogger<WakeOnLanService> _logger;
    public WakeOnLanService(ILogger<WakeOnLanService> logger) { _logger = logger; }

    public bool TryWake(string macAddress)
    {
        var bytes = ParseMac(macAddress);
        if (bytes is null)
        {
            _logger.LogWarning("MAC tidak valid: {Mac}", macAddress);
            return false;
        }

        var packet = new byte[6 + 16 * 6];
        for (int i = 0; i < 6; i++) packet[i] = 0xFF;
        for (int i = 0; i < 16; i++) Buffer.BlockCopy(bytes, 0, packet, 6 + i * 6, 6);

        try
        {
            using var udp = new UdpClient { EnableBroadcast = true };
            // Kirim ke semua broadcast subnet supaya menjangkau VLAN/segmen LAN.
            udp.Send(packet, packet.Length, new IPEndPoint(IPAddress.Broadcast, 9));
            udp.Send(packet, packet.Length, new IPEndPoint(IPAddress.Broadcast, 7));
            _logger.LogInformation("Magic packet dikirim ke {Mac}", macAddress);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Gagal kirim magic packet");
            return false;
        }
    }

    private static byte[]? ParseMac(string mac)
    {
        try
        {
            var phys = PhysicalAddress.Parse(mac.Replace("-", ":").ToUpperInvariant().Replace(":", "-"));
            var bytes = phys.GetAddressBytes();
            return bytes.Length == 6 ? bytes : null;
        }
        catch { return null; }
    }
}
