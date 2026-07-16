using System.IO;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Net;
using System.Security.Cryptography;
using LabKom.Shared.Contracts;
using LabKom.Shared.Hub;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace LabKom.Teacher.Services;

/// <summary>
/// Mengelola file yang dibagi guru: copy ke folder share, hitung SHA-256,
/// kirim notice ke target student via Hub. Kestrel mengexpose folder share
/// di endpoint /files/{noticeId}/{fileName}.
/// </summary>
public class FileDistributionService
{
    private readonly HubContextHolder _hub;
    private readonly ILogger<FileDistributionService> _logger;
    private readonly int _hubPort;

    public string ShareFolder { get; }

    public FileDistributionService(
        HubContextHolder hub,
        IConfiguration config,
        ILogger<FileDistributionService> logger)
    {
        _hub = hub;
        _logger = logger;
        _hubPort = config.GetValue("Teacher:HubPort", 41235);

        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        ShareFolder = Path.Combine(local, "LabKom", "SharedFiles");
        Directory.CreateDirectory(ShareFolder);
    }

    public async Task<FileDistributionNotice?> ShareFileAsync(string sourcePath, string? targetPcName)
    {
        if (!File.Exists(sourcePath))
        {
            _logger.LogWarning("File tidak ditemukan: {Path}", sourcePath);
            return null;
        }

        var noticeId = Guid.NewGuid().ToString("N");
        var fileName = Path.GetFileName(sourcePath);
        var folder = Path.Combine(ShareFolder, noticeId);
        Directory.CreateDirectory(folder);

        var destPath = Path.Combine(folder, fileName);
        File.Copy(sourcePath, destPath, overwrite: true);

        var sha = await ComputeSha256Async(destPath);
        var size = new FileInfo(destPath).Length;
        var url = $"https://{GetLanIp()}:{_hubPort}/files/{noticeId}/{Uri.EscapeDataString(fileName)}";

        var notice = new FileDistributionNotice(
            noticeId, fileName, size, sha, url, targetPcName,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

        await DispatchNoticeAsync(notice);
        _logger.LogInformation("File '{Name}' ({Size}B) dibagikan ke {Target}",
            fileName, size, targetPcName ?? "SEMUA");
        return notice;
    }

    private Task DispatchNoticeAsync(FileDistributionNotice notice)
    {
        var hub = _hub.HubContext;
        if (hub is null) return Task.CompletedTask;
        if (string.IsNullOrEmpty(notice.TargetPcName))
        {
            return hub.Clients
                .Group(HubRoutes.Groups.ForRole(HubRoutes.Roles.Desktop))
                .SendAsync(HubRoutes.Methods.ReceiveFileNotice, notice);
        }
        return hub.Clients
            .Group(HubRoutes.Groups.ForPcRole(notice.TargetPcName, HubRoutes.Roles.Desktop))
            .SendAsync(HubRoutes.Methods.ReceiveFileNotice, notice);
    }

    private static async Task<string> ComputeSha256Async(string path)
    {
        await using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string GetLanIp()
    {
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces()
                     .Where(n => n.OperationalStatus == OperationalStatus.Up
                                 && n.NetworkInterfaceType != NetworkInterfaceType.Loopback))
        {
            var addr = nic.GetIPProperties().UnicastAddresses
                .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork
                                     && !IPAddress.IsLoopback(a.Address));
            if (addr is not null) return addr.Address.ToString();
        }
        return "127.0.0.1";
    }
}
