using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using LabKom.Shared.Contracts;
using LabKom.Shared.Hub;
using LabKom.Shared.Security;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace LabKom.Teacher.Services;

/// <summary>
/// Mengelola file yang dibagi guru: copy ke folder share, hitung SHA-256,
/// kirim notice ke target student via Hub. Kestrel mengexpose folder share
/// di endpoint /files/{noticeId}/{fileName}.
/// </summary>
public class FileDistributionService
{
    private const long MaximumFileBytes = 200L * 1024 * 1024;

    private readonly HubContextHolder _hub;
    private readonly ILogger<FileDistributionService> _logger;
    private readonly int _hubPort;
    private readonly TimeSpan _shareRetention;
    private readonly TeacherAuthorizationService _authorization;

    public string ShareFolder { get; }

    public FileDistributionService(
        HubContextHolder hub,
        IConfiguration config,
        ILogger<FileDistributionService> logger,
        TeacherAuthorizationService authorization)
    {
        _hub = hub;
        _logger = logger;
        _authorization = authorization;
        _hubPort = config.GetValue("Teacher:HubPort", 41235);
        var retentionHours = Math.Clamp(
            config.GetValue("Teacher:SharedFileRetentionHours", 24),
            1,
            168);
        _shareRetention = TimeSpan.FromHours(retentionHours);

        var local = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        ShareFolder = Path.Combine(local, "LabKom", "SharedFiles");
        Directory.CreateDirectory(ShareFolder);
        CleanupExpiredShares();
    }

    public async Task<FileDistributionNotice?> ShareFileAsync(
        string sourcePath,
        string? targetPcName)
    {
        _authorization.Demand(
            TeacherPermission.DistributeFiles,
            "file.distribute",
            targetPcName);
        if (targetPcName is not null
            && !HubSecurity.IsValidPcName(targetPcName))
        {
            throw new ArgumentException(
                "Target PC distribusi file tidak valid.",
                nameof(targetPcName));
        }

        var notice = await PrepareNoticeAsync(sourcePath, targetPcName);
        if (notice is null) return null;

        await DispatchNoticeAsync(
            notice,
            targetPcName is null ? null : new[] { targetPcName });
        _logger.LogInformation(
            "File '{Name}' ({Size}B) dibagikan ke {Target}",
            notice.FileName,
            notice.SizeBytes,
            targetPcName ?? "SEMUA");
        return notice;
    }

    public async Task<FileDistributionNotice?> ShareFileToTargetsAsync(
        string sourcePath,
        IEnumerable<string> targetPcNames)
    {
        var targets = NormalizeTargets(targetPcNames);
        _authorization.Demand(
            TeacherPermission.DistributeFiles,
            "file.distribute.multiple",
            null);
        var notice = await PrepareNoticeAsync(
            sourcePath,
            targetPcName: null);
        if (notice is null) return null;

        await DispatchNoticeAsync(notice, targets);
        _logger.LogInformation(
            "File '{Name}' ({Size}B) dibagikan ke {Count} PC terpilih",
            notice.FileName,
            notice.SizeBytes,
            targets.Length);
        return notice;
    }

    public int CleanupExpiredShares(DateTimeOffset? now = null)
    {
        var removed = 0;
        var cutoffUtc =
            (now ?? DateTimeOffset.UtcNow).UtcDateTime - _shareRetention;
        var root = Path.GetFullPath(ShareFolder)
            .TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar)
                   + Path.DirectorySeparatorChar;

        foreach (var directory in Directory.EnumerateDirectories(ShareFolder))
        {
            try
            {
                var fullPath = Path.GetFullPath(directory);
                var name = Path.GetFileName(fullPath);
                if (!fullPath.StartsWith(
                        root,
                        StringComparison.OrdinalIgnoreCase)
                    || !Guid.TryParseExact(name, "N", out _))
                {
                    continue;
                }

                var info = new DirectoryInfo(fullPath);
                if (info.LastWriteTimeUtc >= cutoffUtc) continue;

                Directory.Delete(fullPath, recursive: true);
                removed++;
            }
            catch (Exception ex) when (
                ex is IOException
                    or UnauthorizedAccessException
                    or System.Security.SecurityException)
            {
                _logger.LogDebug(
                    ex,
                    "Folder share lama gagal dibersihkan: {Directory}",
                    directory);
            }
        }

        if (removed > 0)
        {
            _logger.LogInformation(
                "{Count} folder distribusi file lama dibersihkan",
                removed);
        }

        return removed;
    }

    private async Task<FileDistributionNotice?> PrepareNoticeAsync(
        string sourcePath,
        string? targetPcName)
    {
        CleanupExpiredShares();
        if (!File.Exists(sourcePath))
        {
            _logger.LogWarning("File tidak ditemukan: {Path}", sourcePath);
            return null;
        }

        var sourceInfo = new FileInfo(sourcePath);
        if (sourceInfo.Length is < 0 or > MaximumFileBytes)
        {
            _logger.LogWarning(
                "Ukuran file di luar batas 200 MiB: {Path}",
                sourcePath);
            return null;
        }

        var noticeId = Guid.NewGuid().ToString("N");
        var fileName = Path.GetFileName(sourcePath);
        var folder = Path.Combine(ShareFolder, noticeId);
        Directory.CreateDirectory(folder);

        try
        {
            var destinationPath = Path.Combine(folder, fileName);
            File.Copy(sourcePath, destinationPath, overwrite: false);

            var size = new FileInfo(destinationPath).Length;
            if (size > MaximumFileBytes)
            {
                throw new InvalidDataException(
                    "File hasil salin melebihi batas 200 MiB.");
            }

            var sha = await ComputeSha256Async(destinationPath);
            var url =
                $"https://{GetLanIp()}:{_hubPort}/files/{noticeId}/" +
                Uri.EscapeDataString(fileName);

            return new FileDistributionNotice(
                noticeId,
                fileName,
                size,
                sha,
                url,
                targetPcName,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        }
        catch
        {
            var fullFolder = Path.GetFullPath(folder);
            var root = Path.GetFullPath(ShareFolder)
                .TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar)
                       + Path.DirectorySeparatorChar;
            if (fullFolder.StartsWith(
                    root,
                    StringComparison.OrdinalIgnoreCase)
                && Directory.Exists(fullFolder))
            {
                Directory.Delete(fullFolder, recursive: true);
            }

            throw;
        }
    }

    private Task DispatchNoticeAsync(
        FileDistributionNotice notice,
        IReadOnlyCollection<string>? targetPcNames)
    {
        var hub = _hub.HubContext;
        if (hub is null) return Task.CompletedTask;

        if (targetPcNames is null)
        {
            return hub.Clients
                .Group(HubRoutes.Groups.ForRole(HubRoutes.Roles.Desktop))
                .SendAsync(HubRoutes.Methods.ReceiveFileNotice, notice);
        }

        var groups = targetPcNames
            .Select(pcName => HubRoutes.Groups.ForPcRole(
                pcName,
                HubRoutes.Roles.Desktop))
            .ToArray();
        var audience = groups.Length == 1
            ? hub.Clients.Group(groups[0])
            : hub.Clients.Groups(groups);
        return audience.SendAsync(
            HubRoutes.Methods.ReceiveFileNotice,
            notice);
    }

    private static string[] NormalizeTargets(
        IEnumerable<string> targetPcNames)
    {
        ArgumentNullException.ThrowIfNull(targetPcNames);
        var targets = targetPcNames
            .Select(pcName => pcName?.Trim() ?? string.Empty)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(pcName => pcName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (targets.Length == 0
            || targets.Any(pcName => !HubSecurity.IsValidPcName(pcName)))
        {
            throw new ArgumentException(
                "Audience file wajib berisi nama PC yang valid.",
                nameof(targetPcNames));
        }

        return targets;
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
                     .Where(nic =>
                         nic.OperationalStatus == OperationalStatus.Up
                         && nic.NetworkInterfaceType
                         != NetworkInterfaceType.Loopback))
        {
            var address = nic.GetIPProperties().UnicastAddresses
                .FirstOrDefault(candidate =>
                    candidate.Address.AddressFamily
                    == AddressFamily.InterNetwork
                    && !IPAddress.IsLoopback(candidate.Address));
            if (address is not null) return address.Address.ToString();
        }

        return "127.0.0.1";
    }
}