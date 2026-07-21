using System.Diagnostics;
using System.Runtime.InteropServices;
using LabKom.Student.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LabKom.Student.Workers;

/// <summary>
/// Runs inside the LocalSystem Student Agent and restarts the interactive
/// Student Desktop scheduled task when it is missing from the console session.
/// </summary>
public sealed class StudentDesktopWatchdog : BackgroundService
{
    private const string DesktopProcessName = "LabKom.Student.Desktop";
    private readonly DesktopWatchdogState _state = new();
    private readonly ILogger<StudentDesktopWatchdog> _logger;
    private readonly bool _enabled;
    private readonly string _taskName;
    private readonly TimeSpan _checkInterval;
    private readonly TimeSpan _missingGrace;
    private readonly TimeSpan _restartCooldown;

    public StudentDesktopWatchdog(
        IConfiguration configuration,
        ILogger<StudentDesktopWatchdog> logger)
    {
        _logger = logger;
        _enabled = configuration.GetValue("DesktopWatchdog:Enabled", true);
        _taskName = configuration["DesktopWatchdog:ScheduledTaskName"]
                    ?? "LabKomStudentDesktop";
        _checkInterval = TimeSpan.FromSeconds(Math.Clamp(
            configuration.GetValue("DesktopWatchdog:CheckIntervalSeconds", 5),
            2,
            300));
        _missingGrace = TimeSpan.FromSeconds(Math.Clamp(
            configuration.GetValue("DesktopWatchdog:MissingGraceSeconds", 10),
            0,
            600));
        _restartCooldown = TimeSpan.FromSeconds(Math.Clamp(
            configuration.GetValue("DesktopWatchdog:RestartCooldownSeconds", 30),
            5,
            3600));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_enabled)
        {
            _logger.LogInformation("Student Desktop watchdog dinonaktifkan oleh konfigurasi");
            return;
        }

        if (!OperatingSystem.IsWindows())
        {
            _logger.LogWarning("Student Desktop watchdog hanya tersedia di Windows");
            return;
        }

        try
        {
            if (StudentRecoveryRegistration.EnsureEmergencyShortcut())
                _logger.LogInformation("Shortcut emergency unlock administrator tersedia");
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Shortcut emergency unlock gagal dibuat; tool tetap dapat dijalankan dari folder instalasi");
        }
        _logger.LogInformation(
            "Student Desktop watchdog aktif untuk task {TaskName}",
            _taskName);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var sessionId = GetActiveConsoleSessionId();
                var running = sessionId is { } activeSession
                              && IsDesktopRunningInSession(activeSession);
                if (_state.ShouldAttemptStart(
                        sessionId.HasValue,
                        running,
                        DateTimeOffset.UtcNow,
                        _missingGrace,
                        _restartCooldown))
                {
                    var started = RunScheduledTask(_taskName);
                    if (started)
                    {
                        _logger.LogWarning(
                            "Student Desktop tidak aktif di session {SessionId}; task dijalankan ulang",
                            sessionId);
                    }
                    else
                    {
                        _logger.LogError(
                            "Watchdog gagal menjalankan task Student Desktop {TaskName}",
                            _taskName);
                    }
                }

                await Task.Delay(_checkInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Loop Student Desktop watchdog gagal");
                await Task.Delay(_checkInterval, stoppingToken);
            }
        }
    }

    private static int? GetActiveConsoleSessionId()
    {
        var value = WTSGetActiveConsoleSessionId();
        return value == uint.MaxValue ? null : checked((int)value);
    }

    private static bool IsDesktopRunningInSession(int sessionId)
    {
        foreach (var process in Process.GetProcessesByName(DesktopProcessName))
        {
            using (process)
            {
                try
                {
                    if (!process.HasExited && process.SessionId == sessionId)
                        return true;
                }
                catch (InvalidOperationException)
                {
                    // Process exited while it was being inspected.
                }
            }
        }

        return false;
    }

    private static bool RunScheduledTask(string taskName)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "schtasks.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            ArgumentList = { "/Run", "/TN", taskName },
        });
        if (process is null) return false;
        if (!process.WaitForExit(15_000))
        {
            process.Kill(entireProcessTree: true);
            return false;
        }

        return process.ExitCode == 0;
    }

    [DllImport("kernel32.dll")]
    private static extern uint WTSGetActiveConsoleSessionId();
}
