using System.IO;
using System.Text;
using LabKom.Shared.Contracts;
using Microsoft.Extensions.Logging;

namespace LabKom.Student.Services;

/// <summary>
/// Applies a validated blacklist to a managed section of the Windows hosts file.
/// The Student Agent runs as LocalSystem, so policy writes stay outside the user
/// desktop process and can be acknowledged to the Teacher.
/// </summary>
public class WebFilterEnforcer
{
    private const string MarkerStart = "# === LABKOM_FILTER_BEGIN ===";
    private const string MarkerEnd = "# === LABKOM_FILTER_END ===";
    private static readonly string HostsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.System),
        "drivers", "etc", "hosts");

    private readonly ILogger<WebFilterEnforcer> _logger;

    public WebFilterEnforcer(ILogger<WebFilterEnforcer> logger)
    {
        _logger = logger;
    }

    public CommandExecutionOutcome Apply(WebFilterPolicy policy)
    {
        try
        {
            if (!File.Exists(HostsPath))
            {
                var message = $"Hosts file tidak ditemukan: {HostsPath}";
                _logger.LogWarning("{Message}", message);
                return new CommandExecutionOutcome(false, message);
            }

            var existing = File.ReadAllLines(HostsPath, Encoding.UTF8);
            var cleaned = StripManagedSection(existing).ToList();

            if (policy.Mode == WebFilterMode.Disabled)
            {
                File.WriteAllLines(HostsPath, cleaned, new UTF8Encoding(false));
                FlushDnsCache();
                _logger.LogInformation("Web filter dinonaktifkan dan hosts dibersihkan");
                return new CommandExecutionOutcome(true, "Blokir situs dinonaktifkan");
            }

            if (policy.Mode != WebFilterMode.Blacklist)
            {
                return new CommandExecutionOutcome(
                    false,
                    "Mode web filter belum didukung oleh Agent");
            }

            cleaned.Add(MarkerStart);
            cleaned.Add($"# Generated: {DateTimeOffset.UtcNow:O}");
            foreach (var domain in policy.Domains)
            {
                cleaned.Add($"127.0.0.1 {domain}");
                if (!domain.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
                {
                    cleaned.Add($"127.0.0.1 www.{domain}");
                }
            }

            cleaned.Add(MarkerEnd);
            File.WriteAllLines(HostsPath, cleaned, new UTF8Encoding(false));
            FlushDnsCache();
            _logger.LogInformation(
                "Web filter diterapkan: {Count} domain",
                policy.Domains.Count);
            return new CommandExecutionOutcome(
                true,
                $"{policy.Domains.Count} domain diblokir");
        }
        catch (UnauthorizedAccessException)
        {
            const string message =
                "Gagal menulis hosts; Student Agent harus berjalan sebagai LocalSystem";
            _logger.LogError("{Message}", message);
            return new CommandExecutionOutcome(false, message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WebFilterEnforcer error");
            return new CommandExecutionOutcome(false, ex.Message);
        }
    }

    private static IEnumerable<string> StripManagedSection(IEnumerable<string> lines)
    {
        var inSection = false;
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

            if (!inSection)
            {
                yield return line;
            }
        }
    }

    private void FlushDnsCache()
    {
        try
        {
            using var process = System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "ipconfig.exe",
                    Arguments = "/flushdns",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                });
            process?.WaitForExit(3_000);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Flush DNS gagal");
        }
    }
}