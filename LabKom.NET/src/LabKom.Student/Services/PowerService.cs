using System.Diagnostics;
using LabKom.Shared.Contracts;
using Microsoft.Extensions.Logging;

namespace LabKom.Student.Services;

public sealed record CommandExecutionOutcome(bool Success, string Message);

/// <summary>Executes validated Windows power commands through shutdown.exe.</summary>
public sealed class PowerService
{
    private readonly ILogger<PowerService> _logger;

    public PowerService(ILogger<PowerService> logger)
    {
        _logger = logger;
    }

    public CommandExecutionOutcome Execute(PowerCommand command)
    {
        var arguments = command.Action switch
        {
            PowerAction.Shutdown => $"/s /t {command.DelaySeconds} /f",
            PowerAction.Restart => $"/r /t {command.DelaySeconds} /f",
            PowerAction.LogOff => "/l",
            _ => null,
        };
        if (arguments is null)
        {
            return new CommandExecutionOutcome(false, "Power action tidak dikenal");
        }

        if (!string.IsNullOrEmpty(command.Reason))
        {
            arguments += $" /c \"{command.Reason.Replace("\"", string.Empty)}\"";
        }

        try
        {
            _logger.LogWarning(
                "Mengeksekusi PowerCommand {CommandId}: {Action} delay {Delay}s",
                command.CommandId,
                command.Action,
                command.DelaySeconds);
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "shutdown.exe",
                Arguments = arguments,
                CreateNoWindow = true,
                UseShellExecute = false,
            });

            return process is null
                ? new CommandExecutionOutcome(false, "shutdown.exe tidak dapat dimulai")
                : new CommandExecutionOutcome(true, "Perintah power diterima Windows");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Gagal menjalankan shutdown.exe");
            return new CommandExecutionOutcome(false, ex.Message);
        }
    }
}
