using System.Diagnostics;
using LabKom.Shared.Contracts;
using Microsoft.Extensions.Logging;

namespace LabKom.Student.Services;

/// <summary>
/// Menyimpan AppBlockPolicy aktif dan menyediakan operasi scan-and-kill
/// untuk nama process yang masuk daftar blokir. Polling dilakukan oleh AppBlockWorker.
/// </summary>
public class AppBlockEnforcer
{
    private readonly ILogger<AppBlockEnforcer> _logger;
    private AppBlockPolicy _policy = AppBlockPolicy.Disabled;

    public AppBlockEnforcer(ILogger<AppBlockEnforcer> logger) { _logger = logger; }

    public AppBlockPolicy CurrentPolicy => _policy;

    public void UpdatePolicy(AppBlockPolicy policy)
    {
        _policy = policy;
        _logger.LogInformation("AppBlockPolicy diupdate: enabled={Enabled} count={Count}",
            policy.Enabled, policy.ProcessNames.Count);
    }

    /// <summary>Bunuh semua process yang namanya cocok policy. Return jumlah yang ter-kill.</summary>
    public int ScanAndKill()
    {
        if (!_policy.Enabled || _policy.ProcessNames.Count == 0) return 0;

        int killed = 0;
        foreach (var name in _policy.ProcessNames)
        {
            try
            {
                var processes = Process.GetProcessesByName(name);
                foreach (var p in processes)
                {
                    try
                    {
                        p.Kill(entireProcessTree: true);
                        killed++;
                        _logger.LogInformation("Killed process {Name} (PID {Pid})", name, p.Id);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Gagal kill {Name} PID {Pid}", name, p.Id);
                    }
                    finally { p.Dispose(); }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "GetProcessesByName({Name}) error", name);
            }
        }
        return killed;
    }
}
