using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using LabKom.Shared.Contracts;
using LabKom.Shared.Hub;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace LabKom.Student.Services;

/// <summary>
/// Download file dari URL yang diberikan FileDistributionNotice ke folder
/// %USERPROFILE%\Documents\LabKom\Received. Verifikasi SHA-256 sebelum claim sukses.
/// </summary>
public class FileDownloader
{
    private readonly HttpClient _http;
    private readonly ILogger<FileDownloader> _logger;
    private readonly string _sharedSecret;
    public string DownloadFolder { get; }

    public FileDownloader(HttpClient http, ILogger<FileDownloader> logger, IConfiguration configuration)
    {
        _http = http;
        _logger = logger;
        _sharedSecret = Environment.GetEnvironmentVariable("LABKOM_SHARED_SECRET")
                        ?? configuration["Agent:SharedSecret"]
                        ?? string.Empty;
        DownloadFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "LabKom", "Received");
        Directory.CreateDirectory(DownloadFolder);
    }

    public async Task<DownloadResult> DownloadAsync(FileDistributionNotice notice, CancellationToken ct)
    {
        var safeName = Path.GetFileName(notice.FileName);
        var dest = Path.Combine(DownloadFolder, safeName);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, notice.DownloadUrl);
            request.Headers.TryAddWithoutValidation(HubSecurity.HeaderName, _sharedSecret);
            using var resp = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            resp.EnsureSuccessStatusCode();

            await using (var src = await resp.Content.ReadAsStreamAsync(ct))
            await using (var dst = File.Create(dest))
            {
                await src.CopyToAsync(dst, ct);
            }

            var sha = await ComputeSha256Async(dest, ct);
            if (!string.Equals(sha, notice.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("SHA-256 mismatch untuk {File}", notice.FileName);
                File.Delete(dest);
                return new DownloadResult(false, dest, "SHA-256 tidak cocok");
            }

            _logger.LogInformation("File '{File}' tersimpan di {Path}", notice.FileName, dest);
            return new DownloadResult(true, dest, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Download gagal: {Url}", notice.DownloadUrl);
            return new DownloadResult(false, dest, ex.Message);
        }
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(stream, ct);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

public sealed record DownloadResult(bool Success, string LocalPath, string? Error);
