using System.Text.Json;

namespace LabKom.Updater;

internal sealed record ReleaseDescriptor(
    int SchemaVersion,
    string Component,
    string Version)
{
    public static ReleaseDescriptor Read(string directory)
    {
        var path = Path.Combine(directory, "release.json");
        if (!File.Exists(path))
            throw new InvalidDataException("Paket tidak memiliki release.json.");

        var descriptor = JsonSerializer.Deserialize<ReleaseDescriptor>(
            File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("release.json tidak valid.");
        if (descriptor.SchemaVersion != 1 ||
            !System.Version.TryParse(descriptor.Version, out _))
            throw new InvalidDataException("Metadata versi paket tidak valid.");
        return descriptor;
    }
}
