using System.Runtime.InteropServices;

namespace LabKom.Installer;

internal static class StudentEmergencyShortcut
{
    private const string ShortcutName = "LabKom Emergency Unlock (Admin).lnk";

    public static void Create(string executable)
    {
        var directory = ShortcutDirectory();
        Directory.CreateDirectory(directory);
        var link = Path.Combine(directory, ShortcutName);

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

    public static void Delete()
    {
        var path = Path.Combine(ShortcutDirectory(), ShortcutName);
        if (File.Exists(path)) File.Delete(path);
    }

    private static string ShortcutDirectory() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
        "Programs",
        "LabKom");
}
