using System.Diagnostics;
using LabKom.Shared.Contracts;
using Microsoft.Extensions.Logging;

namespace LabKom.Student.Services;

/// <summary>
/// Stores the active process block policy and terminates matching process trees.
/// </summary>
public class AppBlockEnforcer
{
    private readonly ILogger<AppBlockEnforcer> _logger;
    private AppBlockPolicy _policy = AppBlockPolicy.Disabled;

    public AppBlockEnforcer(ILogger<AppBlockEnforcer> logger)
    {
        _logger = logger;
    }

    public AppBlockPolicy CurrentPolicy => _policy;

    public CommandExecutionOutcome UpdatePolicy(AppBlockPolicy policy)
    {
        _policy = policy;
        _logger.LogInformation(
            "AppBlockPolicy diperbarui: enabled={Enabled} count={Count}",
            policy.Enabled,
            policy.ProcessNames.Count);
        return new CommandExecutionOutcome(
            true,
            policy.Enabled
                ? $"{policy.ProcessNames.Count} aplikasi diblokir"
                : "Blokir aplikasi dinonaktifkan");
    }

    public int ScanAndKill()
    {
        if (!_policy.Enabled || _policy.ProcessNames.Count == 0) return 0;

        var killed = 0;
        foreach (var name in _policy.ProcessNames)
        {
            try
            {
                var processes = Process.GetProcessesByName(name);
                foreach (var process in processes)
                {
                    try
                    {
                        process.Kill(entireProcessTree: true);
                        killed++;
                        _logger.LogInformation(
                            "Menghentikan process {Name} (PID {Pid})",
                            name,
                            process.Id);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(
                            ex,
                            "Gagal menghentikan {Name} PID {Pid}",
                            name,
                            process.Id);
                    }
                    finally
                    {
                        process.Dispose();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "GetProcessesByName({Name}) gagal", name);
            }
        }

        return killed;
    }
}