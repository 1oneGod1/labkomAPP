using System.Text.Json;

namespace LabKom.Shared.Updates;

public static class UpdateHealthReporter
{
    public static string StateDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "LabKom",
        "Updates");

    public static string GetPendingPath(string component) => Path.Combine(
        StateDirectory,
        $"pending-{NormalizeComponent(component)}.json");

    public static void MarkHealthy(string component, string version)
    {
        var pendingPath = GetPendingPath(component);
        if (!File.Exists(pendingPath)) return;

        var pending = JsonSerializer.Deserialize<PendingUpdate>(File.ReadAllText(pendingPath));
        if (pending is null || !string.Equals(pending.Version, version, StringComparison.OrdinalIgnoreCase))
            return;

        File.Delete(pendingPath);
    }

    public static void WritePending(PendingUpdate pending)
    {
        Directory.CreateDirectory(StateDirectory);
        var target = GetPendingPath(pending.Component);
        var temporary = target + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(pending, JsonOptions));
        File.Move(temporary, target, overwrite: true);
    }

    public static PendingUpdate? ReadPending(string component)
    {
        var path = GetPendingPath(component);
        return File.Exists(path)
            ? JsonSerializer.Deserialize<PendingUpdate>(File.ReadAllText(path))
            : null;
    }

    private static string NormalizeComponent(string component)
    {
        var value = component.Trim().ToLowerInvariant();
        if (value is not ("student" or "teacher"))
            throw new ArgumentOutOfRangeException(nameof(component), "Komponen harus Student atau Teacher.");
        return value;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };
}

public sealed record PendingUpdate(
    string Component,
    string Version,
    string InstallDirectory,
    string BackupDirectory,
    DateTimeOffset InstalledAtUtc);
