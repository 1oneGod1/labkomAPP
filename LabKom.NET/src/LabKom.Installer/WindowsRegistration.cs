using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security;
using System.ServiceProcess;
using System.Text;
using Microsoft.Win32;

namespace LabKom.Installer;

internal static class WindowsRegistration
{
    public const string ServiceName = "LabKomStudentAgent";
    public const string DesktopTaskName = "LabKomStudentDesktop";
    private const string StudentUpdateTaskName = "LabKomStudentUpdate";
    private const string TeacherUpdateTaskName = "LabKomTeacherUpdate";
    private const string UninstallRoot = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    public static void PrepareForInstall()
    {
        RunTool("schtasks.exe", ["/End", "/TN", StudentUpdateTaskName], acceptFailure: true);
        RunTool("schtasks.exe", ["/End", "/TN", TeacherUpdateTaskName], acceptFailure: true);
        foreach (var process in Process.GetProcessesByName("LabKom.Updater"))
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
                    // The scheduled updater already exited.
                }
            }
        }
    }

    public static void Register(
        InstallerOptions options,
        string root,
        string version,
        InstallerLogger logger)
    {
        var componentDirectory = Path.Combine(root, options.Component);
        var updaterDirectory = Path.Combine(root, "Updater");
        var updaterExecutable = Path.Combine(updaterDirectory, "LabKom.Updater.exe");
        var publicKeyPath = Path.Combine(updaterDirectory, "update-public.cer");

        if (options.Component == "Student")
            RegisterStudent(componentDirectory, logger);
        else
            RegisterTeacher(componentDirectory, logger);

        if (options.UpdatesEnabled)
        {
            RegisterUpdateTask(
                options.Component,
                componentDirectory,
                updaterExecutable,
                publicKeyPath,
                options.UpdateUrl!);
        }
        else
        {
            DeleteTask(options.Component == "Student" ? StudentUpdateTaskName : TeacherUpdateTaskName);
        }

        RegisterUninstaller(options, root, version);
    }

    public static void Unregister(string component, InstallerLogger logger)
    {
        DeleteTask(component == "Student" ? StudentUpdateTaskName : TeacherUpdateTaskName);
        if (component == "Student")
        {
            DeleteTask(DesktopTaskName);
            StopAndDeleteService();
            DeleteFirewallRule("LabKom Student Discovery Agent");
            DeleteFirewallRule("LabKom Student Discovery Desktop");
            StudentEmergencyShortcut.Delete();
        }
        else
        {
            DeleteFirewallRule("LabKom Teacher Hub");
            DeleteTeacherShortcut();
        }

        Registry.LocalMachine.DeleteSubKeyTree(
            $@"{UninstallRoot}\LabKom{component}",
            throwOnMissingSubKey: false);
        logger.Info($"Registrasi Windows {component} dibersihkan.");
    }

    public static bool IsInstalled(string component)
    {
        using var key = Registry.LocalMachine.OpenSubKey($@"{UninstallRoot}\LabKom{component}");
        return key is not null;
    }

    public static bool RunTeacherHealthCheck(string teacherDirectory)
    {
        var executable = Path.Combine(teacherDirectory, "LabKom.Teacher.exe");
        if (!File.Exists(executable)) return false;
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            CreateNoWindow = true,
            ArgumentList = { "--health-check" },
        });
        return process is not null && process.WaitForExit(30_000) && process.ExitCode == 0;
    }

    private static void RegisterStudent(string componentDirectory, InstallerLogger logger)
    {
        StopAndDeleteService();
        DeleteTask(DesktopTaskName);

        var agent = Path.Combine(componentDirectory, "Agent", "LabKom.Student.Agent.exe");
        var desktop = Path.Combine(componentDirectory, "Desktop", "LabKom.Student.Desktop.exe");
        var emergencyUnlock = Path.Combine(
            componentDirectory,
            "Admin",
            "LabKom.Provisioning.exe");
        if (!File.Exists(agent) || !File.Exists(desktop))
            throw new FileNotFoundException("Executable Student Agent/Desktop tidak lengkap.");

        RunTool(
            "sc.exe",
            ["create", ServiceName, "binPath=", $"\"{agent}\"", "start=", "auto",
             "DisplayName=", "LabKom Student Agent"]);
        RunTool(
            "sc.exe",
            ["description", ServiceName, "Agent pengelolaan komputer lab untuk siswa"]);
        RunTool(
            "sc.exe",
            ["failure", ServiceName, "reset=", "60",
             "actions=", "restart/5000/restart/5000/restart/10000"]);
        RunTool("sc.exe", ["failureflag", ServiceName, "1"]);

        RegisterDesktopTask(desktop);
        AddFirewallRule(
            "LabKom Student Discovery Agent",
            agent,
            "UDP",
            "41234");
        AddFirewallRule(
            "LabKom Student Discovery Desktop",
            desktop,
            "UDP",
            "41234");
        if (File.Exists(emergencyUnlock))
        {
            StudentEmergencyShortcut.Create(emergencyUnlock);
        }
        else
            logger.Info("Tool emergency unlock tidak ada pada paket lama yang dipulihkan.");

        using var service = new ServiceController(ServiceName);
        service.Start();
        service.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));
        RunTool("schtasks.exe", ["/Run", "/TN", DesktopTaskName], acceptFailure: true);
        logger.Info("Windows Service dan autostart Student berhasil didaftarkan.");
    }

    private static void RegisterTeacher(string componentDirectory, InstallerLogger logger)
    {
        var teacher = Path.Combine(componentDirectory, "LabKom.Teacher.exe");
        if (!File.Exists(teacher))
            throw new FileNotFoundException("LabKom.Teacher.exe tidak ditemukan.");

        AddFirewallRule("LabKom Teacher Hub", teacher, "TCP", "41235");
        CreateTeacherShortcut(teacher);
        logger.Info("Shortcut dan firewall Teacher berhasil didaftarkan.");
    }

    private static void RegisterDesktopTask(string executable)
    {
        var command = SecurityElement.Escape(executable)
            ?? throw new InvalidOperationException("Path Desktop tidak valid.");
        var xml =
            $$"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.4" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <Triggers>
                <LogonTrigger><Enabled>true</Enabled></LogonTrigger>
              </Triggers>
              <Principals>
                <Principal id="Author">
                  <GroupId>S-1-5-32-545</GroupId>
                  <RunLevel>LeastPrivilege</RunLevel>
                </Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <AllowHardTerminate>false</AllowHardTerminate>
                <StartWhenAvailable>true</StartWhenAvailable>
                <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
                <AllowStartOnDemand>true</AllowStartOnDemand>
                <Enabled>true</Enabled>
                <Hidden>true</Hidden>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
              </Settings>
              <Actions Context="Author">
                <Exec><Command>{{command}}</Command></Exec>
              </Actions>
            </Task>
            """;
        CreateTaskFromXml(DesktopTaskName, xml);
    }

    private static void RegisterUpdateTask(
        string component,
        string installDirectory,
        string updaterExecutable,
        string publicKeyPath,
        string manifestUrl)
    {
        var taskName = component == "Student" ? StudentUpdateTaskName : TeacherUpdateTaskName;
        DeleteTask(taskName);

        var arguments = new StringBuilder()
            .Append("check --component ").Append(component)
            .Append(" --install-dir \"").Append(installDirectory).Append('"')
            .Append(" --manifest-url \"").Append(manifestUrl).Append('"')
            .Append(" --public-key \"").Append(publicKeyPath).Append('"');
        if (component == "Student")
        {
            arguments.Append(" --service ").Append(ServiceName)
                .Append(" --desktop-task ").Append(DesktopTaskName);
        }

        var commandXml = SecurityElement.Escape(updaterExecutable)!;
        var argumentsXml = SecurityElement.Escape(arguments.ToString())!;
        var start = DateTime.Now.AddMinutes(5).ToString("s", System.Globalization.CultureInfo.InvariantCulture);
        var xml =
            $$"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.4" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <Triggers>
                <BootTrigger><Enabled>true</Enabled><Delay>PT2M</Delay></BootTrigger>
                <CalendarTrigger>
                  <Repetition>
                    <Interval>PT1H</Interval>
                    <Duration>P1D</Duration>
                    <StopAtDurationEnd>false</StopAtDurationEnd>
                  </Repetition>
                  <StartBoundary>{{start}}</StartBoundary>
                  <Enabled>true</Enabled>
                  <ScheduleByDay><DaysInterval>1</DaysInterval></ScheduleByDay>
                </CalendarTrigger>
              </Triggers>
              <Principals>
                <Principal id="System">
                  <UserId>S-1-5-18</UserId>
                  <RunLevel>HighestAvailable</RunLevel>
                </Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <StartWhenAvailable>true</StartWhenAvailable>
                <RunOnlyIfNetworkAvailable>true</RunOnlyIfNetworkAvailable>
                <Enabled>true</Enabled>
                <Hidden>true</Hidden>
                <ExecutionTimeLimit>PT30M</ExecutionTimeLimit>
              </Settings>
              <Actions Context="System">
                <Exec>
                  <Command>{{commandXml}}</Command>
                  <Arguments>{{argumentsXml}}</Arguments>
                </Exec>
              </Actions>
            </Task>
            """;
        CreateTaskFromXml(taskName, xml);
    }

    private static void CreateTaskFromXml(string taskName, string xml)
    {
        var temporary = Path.Combine(Path.GetTempPath(), $"labkom-task-{Guid.NewGuid():N}.xml");
        try
        {
            File.WriteAllText(temporary, xml, Encoding.Unicode);
            RunTool("schtasks.exe", ["/Create", "/TN", taskName, "/XML", temporary, "/F"]);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static void RegisterUninstaller(InstallerOptions options, string root, string version)
    {
        var cacheDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "LabKom",
            "Installer",
            "Cache");
        Directory.CreateDirectory(cacheDirectory);
        var cachedSetup = Path.Combine(cacheDirectory, $"LabKom-{options.Component}-Setup.exe");
        var current = Environment.ProcessPath
            ?? throw new InvalidOperationException("Path setup tidak diketahui.");
        if (!string.Equals(current, cachedSetup, StringComparison.OrdinalIgnoreCase))
            File.Copy(current, cachedSetup, overwrite: true);

        var executable = options.Component == "Teacher"
            ? Path.Combine(root, "Teacher", "LabKom.Teacher.exe")
            : Path.Combine(root, "Student", "Desktop", "LabKom.Student.Desktop.exe");
        using var key = Registry.LocalMachine.CreateSubKey($@"{UninstallRoot}\LabKom{options.Component}", writable: true)
            ?? throw new InvalidOperationException("Registry uninstall tidak dapat dibuat.");
        key.SetValue("DisplayName", $"LabKom {options.Component}");
        key.SetValue("DisplayVersion", version);
        key.SetValue("Publisher", "LabKom");
        key.SetValue("InstallLocation", Path.Combine(root, options.Component));
        key.SetValue("DisplayIcon", executable);
        key.SetValue("UninstallString", $"\"{cachedSetup}\" --uninstall");
        key.SetValue("QuietUninstallString", $"\"{cachedSetup}\" --uninstall --silent");
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        key.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
    }

    private static void StopAndDeleteService()
    {
        try
        {
            using var service = new ServiceController(ServiceName);
            service.Refresh();
            if (service.Status is not (ServiceControllerStatus.Stopped or ServiceControllerStatus.StopPending))
                service.Stop();
            service.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
        }
        catch (InvalidOperationException)
        {
            // Service is not installed.
        }

        RunTool("sc.exe", ["delete", ServiceName], acceptFailure: true);
        Thread.Sleep(500);
    }

    private static void DeleteTask(string taskName)
    {
        RunTool("schtasks.exe", ["/End", "/TN", taskName], acceptFailure: true);
        RunTool("schtasks.exe", ["/Delete", "/TN", taskName, "/F"], acceptFailure: true);
    }

    private static void AddFirewallRule(
        string name,
        string program,
        string protocol,
        string port)
    {
        DeleteFirewallRule(name);
        RunTool(
            "netsh.exe",
            ["advfirewall", "firewall", "add", "rule",
             $"name={name}", "dir=in", "action=allow", $"protocol={protocol}",
             $"localport={port}", "profile=private", $"program={program}", "enable=yes"]);
    }

    private static void DeleteFirewallRule(string name) =>
        RunTool(
            "netsh.exe",
            ["advfirewall", "firewall", "delete", "rule", $"name={name}"],
            acceptFailure: true);

    private static void CreateTeacherShortcut(string executable)
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
            "Programs",
            "LabKom");
        Directory.CreateDirectory(directory);
        var link = Path.Combine(directory, "LabKom Teacher.lnk");

        var shellType = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new InvalidOperationException("Windows Script Host tidak tersedia.");
        dynamic shell = Activator.CreateInstance(shellType)
            ?? throw new InvalidOperationException("Windows Script Host gagal dibuat.");
        dynamic shortcut = shell.CreateShortcut(link);
        try
        {
            shortcut.TargetPath = executable;
            shortcut.WorkingDirectory = Path.GetDirectoryName(executable);
            shortcut.Description = "LabKom Teacher Console";
            shortcut.Save();
        }
        finally
        {
            Marshal.FinalReleaseComObject(shortcut);
            Marshal.FinalReleaseComObject(shell);
        }
    }

    private static void DeleteTeacherShortcut()
    {
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
            "Programs",
            "LabKom",
            "LabKom Teacher.lnk");
        if (File.Exists(path)) File.Delete(path);
    }

    private static string RunTool(
        string fileName,
        IEnumerable<string> arguments,
        bool acceptFailure = false)
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
            ?? throw new InvalidOperationException($"{fileName} tidak dapat dijalankan.");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        if (!process.WaitForExit(30_000))
        {
            process.Kill(entireProcessTree: true);
            throw new System.TimeoutException($"{fileName} melewati batas waktu.");
        }

        if (!acceptFailure && process.ExitCode != 0)
            throw new InvalidOperationException(
                $"{fileName} gagal ({process.ExitCode}): {error.Trim()} {output.Trim()}");
        return output;
    }
}
