using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace LabKom.Shared.Devices;

/// <summary>
/// Mengumpulkan identitas mesin: hostname, IP LAN, MAC address.
/// Snapshot di-cache supaya tidak query NIC tiap heartbeat.
/// </summary>
public class MachineIdentity
{
    public string PcName { get; }
    public string IpAddress { get; }
    public string MacAddress { get; }

    public MachineIdentity()
    {
        PcName = Environment.MachineName;
        (IpAddress, MacAddress) = ResolveLanInterface();
    }

    private static (string ip, string mac) ResolveLanInterface()
    {
        // Pilih NIC pertama yang aktif (Up), bukan loopback, dan punya IPv4 LAN.
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces()
                     .Where(n => n.OperationalStatus == OperationalStatus.Up
                                 && n.NetworkInterfaceType != NetworkInterfaceType.Loopback))
        {
            var ipv4 = nic.GetIPProperties().UnicastAddresses
                .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork
                                     && !IPAddress.IsLoopback(a.Address));
            if (ipv4 is null) continue;

            var mac = nic.GetPhysicalAddress().ToString();
            return (ipv4.Address.ToString(), FormatMac(mac));
        }
        return ("127.0.0.1", "00:00:00:00:00:00");
    }

    private static string FormatMac(string raw) =>
        raw.Length == 12
            ? string.Join(':', Enumerable.Range(0, 6).Select(i => raw.Substring(i * 2, 2)))
            : raw;
}
