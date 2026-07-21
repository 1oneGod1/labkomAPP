using System.Runtime.InteropServices;

namespace LabKom.Updater;

internal static class StudentRecoveryShortcut
{
    public static void EnsureInstalled(string studentDirectory)
    {
        var executable = Path.Combine(
            studentDirectory,
            "Admin",
            "LabKom.Provisioning.exe");
        if (!File.Exists(executable)) return;

        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
            "Programs",
            "LabKom");
        Directory.CreateDirectory(directory);
        var link = Path.Combine(directory, "LabKom Emergency Unlock (Admin).lnk");

        var shellType = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new InvalidOperationException("Windows Script Host tidak tersedia.");
        dynamic shell = Activator.CreateInstance(shellType)
            ?? throw new InvalidOperationException("Windows Script Host gagal dibuat.");
        dynamic shortcut = shell.CreateShortcut(link);
        try
        {
            shortcut.TargetPath = executable;
            shortcut.Arguments =
                "emergency-unlock --minutes 15 --reason \"Start Menu administrator recovery\"";
            shortcut.WorkingDirectory = Path.GetDirectoryName(executable);
            shortcut.Description =
                "Lepas kontrol Student sementara dengan kredensial administrator";
            shortcut.Save();
        }
        finally
        {
            Marshal.FinalReleaseComObject(shortcut);
            Marshal.FinalReleaseComObject(shell);
        }
    }
}
