using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using LabKom.Shared.Contracts;
using LabKom.Shared.Discovery;
using LabKom.Shared.Hub;
using LabKom.Shared.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace LabKom.Student.Desktop.Services;

/// <summary>Downloads classroom files into the interactive user's Documents folder.</summary>
public sealed class FileDownloader
{
    private const long MaximumFileBytes = 200L * 1024 * 1024;
    private const long ProgressReportIntervalBytes = 512L * 1024;
    private const int MaximumConflictCopies = 999;

    private readonly TeacherEndpointStore _endpointStore;
    private readonly ILogger<FileDownloader> _logger;
    private readonly string _sharedSecret;

    public string DownloadFolder { get; }

    public FileDownloader(
        TeacherEndpointStore endpointStore,
        ILogger<FileDownloader> logger,
        IConfiguration configuration)
    {
        _endpointStore = endpointStore;
        _logger = logger;
        _sharedSecret = Environment.GetEnvironmentVariable("LABKOM_SHARED_SECRET")
                        ?? configuration["Desktop:SharedSecret"]
                        ?? string.Empty;
        DownloadFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "LabKom",
            "Received");
        Directory.CreateDirectory(DownloadFolder);
        CleanupStalePartials();
    }

    public Task<DownloadResult> DownloadAsync(
        FileDistributionNotice notice,
        CancellationToken ct) =>
        DownloadAsync(notice, progress: null, ct);

    public async Task<DownloadResult> DownloadAsync(
        FileDistributionNotice notice,
        Func<long, CancellationToken, Task>? progress,
        CancellationToken ct)
    {
        if (!TryValidateNotice(notice, out var uri, out var safeName, out var validationError))
        {
            return new DownloadResult(false, string.Empty, 0, validationError);
        }

        var authentication = DeviceCredentialStore.Resolve(_sharedSecret);
        if (!HubSecurity.IsStrongSecret(authentication.Secret))
        {
            return new DownloadResult(
                false,
                string.Empty,
                0,
                "Shared secret belum dikonfigurasi");
        }

        var destination = GetAvailableDestinationPath(DownloadFolder, safeName);
        var partial = Path.Combine(DownloadFolder, $".{notice.Id}.partial");
        long received = 0;

        try
        {
            var endpoint = _endpointStore.GetFreshSnapshot()?.Beacon;
            if (endpoint is null
                || !string.Equals(uri.Host, endpoint.Ip, StringComparison.OrdinalIgnoreCase)
                || uri.Port != endpoint.HubPort)
            {
                return new DownloadResult(
                    false,
                    destination,
                    received,
                    "Endpoint download tidak cocok dengan Teacher aktif");
            }

            using var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback =
                    (_, certificate, _, _) =>
                        CertificatePin.Matches(
                            certificate,
                            endpoint.CertificateSha256),
            };
            using var http = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromMinutes(10),
            };
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.TryAddWithoutValidation(
                HubSecurity.HeaderName,
                authentication.Secret);
            request.Headers.TryAddWithoutValidation(
                HubSecurity.PcNameHeaderName,
                authentication.PcName);
            if (!authentication.IsLegacy)
            {
                request.Headers.TryAddWithoutValidation(
                    HubSecurity.DeviceIdHeaderName,
                    authentication.DeviceId);
                request.Headers.TryAddWithoutValidation(
                    HubSecurity.KeyVersionHeaderName,
                    authentication.KeyVersion!.Value.ToString(
                        System.Globalization.CultureInfo.InvariantCulture));
            }

            using var response = await http.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                ct);
            response.EnsureSuccessStatusCode();

            if (response.Content.Headers.ContentLength is { } contentLength
                && (contentLength != notice.SizeBytes
                    || contentLength > MaximumFileBytes))
            {
                return new DownloadResult(
                    false,
                    destination,
                    received,
                    "Ukuran file dari server tidak sesuai");
            }

            long lastReported = 0;
            await using (var source = await response.Content.ReadAsStreamAsync(ct))
            await using (var output = new FileStream(
                             partial,
                             FileMode.Create,
                             FileAccess.Write,
                             FileShare.None,
                             65_536,
                             useAsync: true))
            {
                var buffer = new byte[65_536];
                while (true)
                {
                    var read = await source.ReadAsync(buffer, ct);
                    if (read == 0) break;

                    received += read;
                    if (received > notice.SizeBytes
                        || received > MaximumFileBytes)
                    {
                        throw new InvalidDataException(
                            "Payload file melebihi ukuran yang diumumkan.");
                    }

                    await output.WriteAsync(buffer.AsMemory(0, read), ct);
                    if (progress is not null
                        && received - lastReported >= ProgressReportIntervalBytes)
                    {
                        await progress(received, ct);
                        lastReported = received;
                    }
                }

                if (received != notice.SizeBytes)
                {
                    throw new InvalidDataException(
                        "Ukuran file yang diterima tidak sesuai.");
                }

                if (progress is not null && received != lastReported)
                {
                    await progress(received, ct);
                }
            }

            var sha256 = await ComputeSha256Async(partial, ct);
            if (!string.Equals(
                    sha256,
                    notice.Sha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                TryDelete(partial);
                return new DownloadResult(
                    false,
                    destination,
                    received,
                    "SHA-256 tidak cocok");
            }

            destination = MoveToAvailableDestination(
                partial,
                DownloadFolder,
                safeName);
            _logger.LogInformation(
                "File '{File}' tersimpan di {Path}",
                notice.FileName,
                destination);
            return new DownloadResult(true, destination, received, null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            TryDelete(partial);
            throw;
        }
        catch (Exception ex)
        {
            TryDelete(partial);
            _logger.LogError(ex, "Download gagal: {Url}", notice.DownloadUrl);
            return new DownloadResult(
                false,
                destination,
                received,
                ex.Message);
        }
    }

    public static string GetAvailableDestinationPath(
        string directory,
        string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        if (!string.Equals(
                Path.GetFileName(fileName),
                fileName,
                StringComparison.Ordinal)
            || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException("Nama file tidak aman.", nameof(fileName));
        }

        for (var copy = 0; copy <= MaximumConflictCopies; copy++)
        {
            var candidate = BuildDestinationPath(directory, fileName, copy);
            if (!PathExists(candidate)) return candidate;
        }

        throw new IOException(
            $"Tidak dapat menentukan nama tujuan unik untuk '{fileName}'.");
    }

    private static string MoveToAvailableDestination(
        string partial,
        string directory,
        string fileName)
    {
        for (var copy = 0; copy <= MaximumConflictCopies; copy++)
        {
            var destination = BuildDestinationPath(directory, fileName, copy);
            try
            {
                File.Move(partial, destination, overwrite: false);
                return destination;
            }
            catch (IOException) when (PathExists(destination))
            {
                // Nama baru dipakai proses lain; coba nomor berikutnya.
            }
        }

        throw new IOException(
            $"Tidak dapat menyimpan '{fileName}' karena semua nama tujuan terpakai.");
    }

    private static string BuildDestinationPath(
        string directory,
        string fileName,
        int copy)
    {
        if (copy == 0) return Path.Combine(directory, fileName);

        var extension = Path.GetExtension(fileName);
        var baseName = Path.GetFileNameWithoutExtension(fileName);
        return Path.Combine(directory, $"{baseName} ({copy}){extension}");
    }

    private static bool TryValidateNotice(
        FileDistributionNotice notice,
        out Uri uri,
        out string safeName,
        out string error)
    {
        uri = null!;
        safeName = Path.GetFileName(notice.FileName ?? string.Empty);
        error = string.Empty;

        if (!Guid.TryParseExact(notice.Id, "N", out _))
        {
            error = "ID distribusi file tidak valid";
        }
        else if (string.IsNullOrWhiteSpace(safeName)
                 || safeName != notice.FileName)
        {
            error = "Nama file tidak valid";
        }
        else if (safeName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            error = "Nama file mengandung karakter terlarang";
        }
        else if (notice.SizeBytes is < 0 or > MaximumFileBytes)
        {
            error = "Ukuran file tidak valid";
        }
        else if (string.IsNullOrWhiteSpace(notice.Sha256)
                 || notice.Sha256.Length != 64
                 || notice.Sha256.Any(character => !Uri.IsHexDigit(character)))
        {
            error = "SHA-256 tidak valid";
        }
        else if (!Uri.TryCreate(
                     notice.DownloadUrl,
                     UriKind.Absolute,
                     out var parsedUri)
                 || parsedUri.Scheme != Uri.UriSchemeHttps)
        {
            error = "URL download tidak valid";
        }
        else
        {
            uri = parsedUri;
        }

        return error.Length == 0;
    }

    private void CleanupStalePartials()
    {
        var cutoffUtc = DateTime.UtcNow.AddHours(-24);
        try
        {
            foreach (var partial in Directory.EnumerateFiles(
                         DownloadFolder,
                         ".*.partial",
                         SearchOption.TopDirectoryOnly))
            {
                try
                {
                    if (File.GetLastWriteTimeUtc(partial) < cutoffUtc)
                    {
                        File.Delete(partial);
                    }
                }
                catch (Exception ex) when (
                    ex is IOException or UnauthorizedAccessException)
                {
                    _logger.LogDebug(
                        ex,
                        "Partial download lama gagal dibersihkan: {Path}",
                        partial);
                }
            }
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogDebug(
                ex,
                "Folder partial download gagal diperiksa");
        }
    }

    private static bool PathExists(string path) =>
        File.Exists(path) || Directory.Exists(path);
    private static async Task<string> ComputeSha256Async(
        string path,
        CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, ct);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // Cleanup failure is non-fatal; the hidden partial is never executed.
        }
    }
}

public sealed record DownloadResult(
    bool Success,
    string LocalPath,
    long BytesReceived,
    string? Error);