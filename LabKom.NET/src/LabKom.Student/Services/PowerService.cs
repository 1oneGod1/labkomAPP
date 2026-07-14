using System.Diagnostics;
using LabKom.Shared.Contracts;
using Microsoft.Extensions.Logging;

namespace LabKom.Student.Services;

/// <summary>
/// Eksekusi perintah power dari Teacher: shutdown, restart, log off.
/// Pakai shutdown.exe Windows untuk menghindari P/Invoke ExitWindowsEx
/// yang butuh privilege adjustment.
/// </summary>
public class PowerService
{
    private readonly ILogger<PowerService> _logger;

    public PowerService(ILogger<PowerService> logger)
    {
        _logger = logger;
    }

    public void Execute(PowerCommand command)
    {
        var args = command.Action switch
        {
            PowerAction.Shutdown => $"/s /t {command.DelaySeconds} /f",
            PowerAction.Restart => $"/r /t {command.DelaySeconds} /f",
            PowerAction.LogOff => "/l",
            _ => null,
        };
        if (args is null)
        {
            _logger.LogWarning("PowerCommand action tidak dikenali: {Action}", command.Action);
            return;
        }

        if (!string.IsNullOrEmpty(command.Reason))
        {
            args += $" /c \"{command.Reason.Replace("\"", "")}\"";
        }

        try
        {
            _logger.LogWarning("Mengeksekusi PowerCommand: {Action} (delay {Delay}s)", command.Action, command.DelaySeconds);
            Process.Start(new ProcessStartInfo
            {
                FileName = "shutdown.exe",
                Arguments = args,
                CreateNoWindow = true,
                UseShellExecute = false,
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Gagal menjalankan shutdown.exe");
        }
    }
}
