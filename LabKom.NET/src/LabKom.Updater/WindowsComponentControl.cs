using System.Diagnostics;
using System.ServiceProcess;

namespace LabKom.Updater;

internal static class WindowsComponentControl
{
    public static bool IsTeacherRunning() =>
        Process.GetProcessesByName("LabKom.Teacher").Length > 0;

    public static bool IsServiceRunning(string serviceName)
    {
        if (string.IsNullOrWhiteSpace(serviceName)) return false;
        try
        {
            using var service = new ServiceController(serviceName);
            service.Refresh();
            return service.Status == ServiceControllerStatus.Running;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    public static void Stop(UpdateOptions options)
    {
        if (options.Component == "Teacher")
        {
            if (IsTeacherRunning())
                throw new UpdateDeferredException("Teacher masih berjalan; update ditunda sampai aplikasi ditutup.");
            return;
        }

        if (!string.IsNullOrWhiteSpace(options.DesktopTaskName))
            RunTool("schtasks.exe", ["/End", "/TN", options.DesktopTaskName], acceptFailure: true);

        foreach (var process in Process.GetProcessesByName("LabKom.Student.Desktop"))
        {
            using (process)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(10_000);
                }
                catch (InvalidOperationException)
                {
                    // Process exited between enumeration and termination.
                }
            }
        }

        if (string.IsNullOrWhiteSpace(options.ServiceName)) return;
        using var service = new ServiceController(options.ServiceName);
        try
        {
            service.Refresh();
            if (service.Status is not (ServiceControllerStatus.Stopped or ServiceControllerStatus.StopPending))
                service.Stop();
            service.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
        }
        catch (InvalidOperationException exception)
        {
            throw new InvalidOperationException($"Service '{options.ServiceName}' tidak dapat dihentikan.", exception);
        }
    }

    public static void Start(UpdateOptions options)
    {
        if (options.Component == "Teacher") return;
        if (string.IsNullOrWhiteSpace(options.ServiceName))
            throw new InvalidOperationException("Nama service Student belum dikonfigurasi.");

        using var service = new ServiceController(options.ServiceName);
        service.Refresh();
        if (service.Status == ServiceControllerStatus.Stopped) service.Start();
        service.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));

        if (!string.IsNullOrWhiteSpace(options.DesktopTaskName))
            RunTool("schtasks.exe", ["/Run", "/TN", options.DesktopTaskName], acceptFailure: true);

        StudentRecoveryShortcut.EnsureInstalled(options.InstallDirectory);
    }

    public static bool RunTeacherHealthCheck(string installDirectory, TimeSpan timeout)
    {
        var executable = Path.Combine(installDirectory, "LabKom.Teacher.exe");
        if (!File.Exists(executable)) return false;

        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            CreateNoWindow = true,
            ArgumentList = { "--health-check" },
        });
        if (process is null) return false;
        return process.WaitForExit((int)timeout.TotalMilliseconds) && process.ExitCode == 0;
    }

    private static void RunTool(string fileName, IEnumerable<string> arguments, bool acceptFailure)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"{fileName} gagal dijalankan.");
        if (!process.WaitForExit(30_000))
        {
            process.Kill(entireProcessTree: true);
            throw new System.TimeoutException($"{fileName} melewati batas waktu.");
        }

        if (!acceptFailure && process.ExitCode != 0)
            throw new InvalidOperationException(
                $"{fileName} gagal ({process.ExitCode}): {process.StandardError.ReadToEnd()}");
    }
}

internal sealed class UpdateDeferredException(string message) : Exception(message);
