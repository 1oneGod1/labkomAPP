using System.IO;
using System.Text;
using LabKom.Shared.Contracts;
using Microsoft.Extensions.Logging;

namespace LabKom.Student.Services;

/// <summary>
/// Menerapkan WebFilterPolicy via hosts file Windows. Semua entry yang
/// dikelola Labkom diberi marker komentar agar bisa dihapus saat policy berubah.
/// Memerlukan akses tulis ke %SystemRoot%\System32\drivers\etc\hosts —
/// jalankan Student Agent sebagai Administrator atau LocalSystem.
/// </summary>
public class WebFilterEnforcer
{
    private const string MarkerStart = "# === LABKOM_FILTER_BEGIN ===";
    private const string MarkerEnd = "# === LABKOM_FILTER_END ===";
    private static readonly string HostsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.System),
        "drivers", "etc", "hosts");

    private readonly ILogger<WebFilterEnforcer> _logger;

    public WebFilterEnforcer(ILogger<WebFilterEnforcer> logger) { _logger = logger; }

    public void Apply(WebFilterPolicy policy)
    {
        try
        {
            if (!File.Exists(HostsPath))
            {
                _logger.LogWarning("Hosts file tidak ditemukan: {Path}", HostsPath);
                return;
            }

            var existing = File.ReadAllLines(HostsPath, Encoding.UTF8);
            var cleaned = StripManagedSection(existing).ToList();

            if (policy.Mode == WebFilterMode.Disabled || policy.Domains.Count == 0)
            {
                File.WriteAllLines(HostsPath, cleaned, new UTF8Encoding(false));
                _logger.LogInformation("Web filter disabled — hosts dibersihkan");
                return;
            }

            cleaned.Add(MarkerStart);
            cleaned.Add($"# Mode: {policy.Mode} | Generated: {DateTimeOffset.UtcNow:O}");

            // Untuk MVP: blacklist saja diterjemahkan ke hosts entry.
            // Whitelist butuh proxy/DNS-level filter — di luar scope hosts file.
            if (policy.Mode == WebFilterMode.Blacklist)
            {
                foreach (var domain in policy.Domains)
                {
                    cleaned.Add($"127.0.0.1 {domain}");
                    if (!domain.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
                    {
                        cleaned.Add($"127.0.0.1 www.{domain}");
                    }
                }
            }
            else
            {
                _logger.LogInformation("Whitelist mode tidak fully-supported via hosts file. Hanya entry blacklist yang diterapkan.");
            }

            cleaned.Add(MarkerEnd);
            File.WriteAllLines(HostsPath, cleaned, new UTF8Encoding(false));
            FlushDnsCache();
            _logger.LogInformation("Web filter diterapkan: {Count} domain", policy.Domains.Count);
        }
        catch (UnauthorizedAccessException)
        {
            _logger.LogError("Gagal tulis hosts: butuh Administrator. Jalankan service sebagai LocalSystem.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WebFilterEnforcer error");
        }
    }

    private static IEnumerable<string> StripManagedSection(IEnumerable<string> lines)
    {
        bool inSection = false;
        foreach (var line in lines)
        {
            if (line.Trim().Equals(MarkerStart, StringComparison.OrdinalIgnoreCase))
            {
                inSection = true;
                continue;
            }
            if (line.Trim().Equals(MarkerEnd, StringComparison.OrdinalIgnoreCase))
            {
                inSection = false;
                continue;
            }
            if (!inSection) yield return line;
        }
    }

    private void FlushDnsCache()
    {
        try
        {
            using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "ipconfig.exe",
                Arguments = "/flushdns",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
            });
            p?.WaitForExit(3000);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Flush DNS gagal");
        }
    }
}
