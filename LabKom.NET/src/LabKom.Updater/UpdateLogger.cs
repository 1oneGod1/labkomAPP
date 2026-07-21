namespace LabKom.Updater;

internal sealed class UpdateLogger
{
    private readonly string _path;

    public UpdateLogger(string component)
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "LabKom",
            "Updates",
            "Logs");
        Directory.CreateDirectory(directory);
        _path = Path.Combine(directory, $"{component.ToLowerInvariant()}.log");
    }

    public void Info(string message) => Write("INFO", message);

    public void Error(string message) => Write("ERROR", message);

    private void Write(string level, string message)
    {
        var line = $"{DateTimeOffset.Now:O} [{level}] {message}";
        Console.WriteLine(line);
        File.AppendAllText(_path, line + Environment.NewLine);
    }
}
