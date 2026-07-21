namespace LabKom.Updater;

internal enum UpdateCommand
{
    Check,
    Rollback,
}

internal sealed record UpdateOptions(
    UpdateCommand Command,
    string Component,
    string InstallDirectory,
    string? ManifestUrl,
    string? PublicKeyPath,
    string? ServiceName,
    string? DesktopTaskName,
    bool AllowLocalFiles)
{
    public const string Usage =
        """
        LabKom.Updater check --component Student|Teacher --install-dir <folder>
          --manifest-url <https-url> --public-key <pem>
          [--service LabKomStudentAgent] [--desktop-task LabKomStudentDesktop]

        LabKom.Updater rollback --component Student|Teacher --install-dir <folder>
          [--service LabKomStudentAgent] [--desktop-task LabKomStudentDesktop]
        """;

    public static UpdateOptions Parse(string[] args)
    {
        if (args.Length == 0) throw new CommandLineException("Perintah updater belum diberikan.");
        var command = args[0].ToLowerInvariant() switch
        {
            "check" => UpdateCommand.Check,
            "rollback" => UpdateCommand.Rollback,
            _ => throw new CommandLineException($"Perintah updater '{args[0]}' tidak dikenal."),
        };

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 1; index < args.Length; index++)
        {
            var key = args[index];
            if (!key.StartsWith("--", StringComparison.Ordinal))
                throw new CommandLineException($"Argumen '{key}' tidak valid.");

            if (string.Equals(key, "--allow-local-files", StringComparison.OrdinalIgnoreCase))
            {
                flags.Add(key);
                continue;
            }

            if (++index >= args.Length)
                throw new CommandLineException($"Nilai untuk '{key}' belum diberikan.");
            values[key] = args[index];
        }

        var component = Require(values, "--component");
        if (component is not ("Student" or "Teacher") &&
            !component.Equals("Student", StringComparison.OrdinalIgnoreCase) &&
            !component.Equals("Teacher", StringComparison.OrdinalIgnoreCase))
            throw new CommandLineException("Component harus Student atau Teacher.");
        component = component.Equals("Student", StringComparison.OrdinalIgnoreCase) ? "Student" : "Teacher";

        var installDirectory = Path.GetFullPath(Require(values, "--install-dir"));
        var manifestUrl = values.GetValueOrDefault("--manifest-url");
        var publicKey = values.GetValueOrDefault("--public-key");
        if (command == UpdateCommand.Check)
        {
            if (string.IsNullOrWhiteSpace(manifestUrl))
                throw new CommandLineException("--manifest-url wajib untuk check.");
            if (string.IsNullOrWhiteSpace(publicKey))
                throw new CommandLineException("--public-key wajib untuk check.");
        }

        return new UpdateOptions(
            command,
            component,
            installDirectory,
            manifestUrl,
            string.IsNullOrWhiteSpace(publicKey) ? null : Path.GetFullPath(publicKey),
            values.GetValueOrDefault("--service"),
            values.GetValueOrDefault("--desktop-task"),
            flags.Contains("--allow-local-files"));
    }

    private static string Require(IReadOnlyDictionary<string, string> values, string key) =>
        values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new CommandLineException($"{key} wajib diberikan.");
}

internal sealed class CommandLineException(string message) : Exception(message);
