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
    }

    public async Task<DownloadResult> DownloadAsync(FileDistributionNotice notice, CancellationToken ct)
    {
        if (!TryValidateNotice(notice, out var uri, out var safeName, out var validationError))
        {
            return new DownloadResult(false, string.Empty, validationError);
        }
        if (!HubSecurity.IsStrongSecret(_sharedSecret))
        {
            return new DownloadResult(false, string.Empty, "Shared secret belum dikonfigurasi");
        }

        var destination = Path.Combine(DownloadFolder, safeName);
        var partial = Path.Combine(DownloadFolder, $".{notice.Id}.partial");

        try
        {
            var endpoint = _endpointStore.GetFreshSnapshot()?.Beacon;
            if (endpoint is null
                || !string.Equals(uri.Host, endpoint.Ip, StringComparison.OrdinalIgnoreCase)
                || uri.Port != endpoint.HubPort)
            {
                return new DownloadResult(false, destination, "Endpoint download tidak cocok dengan Teacher aktif");
            }

            using var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback =
                    (_, certificate, _, _) => CertificatePin.Matches(certificate, endpoint.CertificateSha256),
            };
            using var http = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(10) };
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.TryAddWithoutValidation(HubSecurity.HeaderName, _sharedSecret);
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            if (response.Content.Headers.ContentLength is { } contentLength
                && (contentLength != notice.SizeBytes || contentLength > MaximumFileBytes))
            {
                return new DownloadResult(false, destination, "Ukuran file dari server tidak sesuai");
            }

            await using (var source = await response.Content.ReadAsStreamAsync(ct))
            await using (var output = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 65_536, useAsync: true))
            {
                var buffer = new byte[65_536];
                long received = 0;
                while (true)
                {
                    var read = await source.ReadAsync(buffer, ct);
                    if (read == 0) break;
                    received += read;
                    if (received > notice.SizeBytes || received > MaximumFileBytes)
                    {
                        throw new InvalidDataException("Payload file melebihi ukuran yang diumumkan.");
                    }
                    await output.WriteAsync(buffer.AsMemory(0, read), ct);
                }

                if (received != notice.SizeBytes)
                {
                    throw new InvalidDataException("Ukuran file yang diterima tidak sesuai.");
                }
            }

            var sha256 = await ComputeSha256Async(partial, ct);
            if (!string.Equals(sha256, notice.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                TryDelete(partial);
                return new DownloadResult(false, destination, "SHA-256 tidak cocok");
            }

            File.Move(partial, destination, overwrite: true);
            _logger.LogInformation("File '{File}' tersimpan di {Path}", notice.FileName, destination);
            return new DownloadResult(true, destination, null);
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
            return new DownloadResult(false, destination, ex.Message);
        }
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

        if (!Guid.TryParseExact(notice.Id, "N", out _)) error = "ID distribusi file tidak valid";
        else if (string.IsNullOrWhiteSpace(safeName) || safeName != notice.FileName) error = "Nama file tidak valid";
        else if (safeName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) error = "Nama file mengandung karakter terlarang";
        else if (notice.SizeBytes is < 0 or > MaximumFileBytes) error = "Ukuran file tidak valid";
        else if (string.IsNullOrWhiteSpace(notice.Sha256) || notice.Sha256.Length != 64 || notice.Sha256.Any(character => !Uri.IsHexDigit(character))) error = "SHA-256 tidak valid";
        else if (!Uri.TryCreate(notice.DownloadUrl, UriKind.Absolute, out var parsedUri)
                 || parsedUri.Scheme != Uri.UriSchemeHttps) error = "URL download tidak valid";
        else uri = parsedUri;

        return error.Length == 0;
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken ct)
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

public sealed record DownloadResult(bool Success, string LocalPath, string? Error);