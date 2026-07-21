namespace LabKom.Installer;

internal sealed class InstallerLogger(bool silent)
{
    private readonly bool _silent = silent;
    private readonly string _path = CreateLogPath();

    public void Info(string message)
    {
        var line = $"{DateTimeOffset.Now:O} [INFO] {message}";
        File.AppendAllText(_path, line + Environment.NewLine);
        if (!_silent) Console.WriteLine(message);
    }

    public void Error(string message)
    {
        var line = $"{DateTimeOffset.Now:O} [ERROR] {message}";
        File.AppendAllText(_path, line + Environment.NewLine);
        Console.Error.WriteLine(message);
    }

    private static string CreateLogPath()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "LabKom",
            "Installer",
            "Logs");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, $"setup-{DateTime.Now:yyyyMMdd-HHmmss}.log");
    }
}
