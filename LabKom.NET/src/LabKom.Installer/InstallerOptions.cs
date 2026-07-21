namespace LabKom.Installer;

internal sealed record InstallerOptions(
    string Component,
    bool Uninstall,
    bool PurgeSecret,
    bool Silent,
    bool UpdatesEnabled,
    string? BundlePath,
    string? ClassroomName,
    string? UpdateUrl)
{
    public static InstallerOptions Parse(string component, string[] args)
    {
        var uninstall = false;
        var purgeSecret = false;
        var silent = false;
        var updatesEnabled = true;
        string? bundle = null;
        string? classroom = null;
        string? updateUrl = component == "Student"
            ? "https://github.com/1oneGod1/labkomAPP/releases/latest/download/LabKom-Student-update.json"
            : "https://github.com/1oneGod1/labkomAPP/releases/latest/download/LabKom-Teacher-update.json";

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index].ToLowerInvariant())
            {
                case "--uninstall":
                    uninstall = true;
                    break;
                case "--purge-secret":
                    purgeSecret = true;
                    break;
                case "--silent":
                    silent = true;
                    break;
                case "--no-updates":
                    updatesEnabled = false;
                    updateUrl = null;
                    break;
                case "--bundle":
                    bundle = RequireValue(args, ref index, "--bundle");
                    break;
                case "--classroom":
                    classroom = RequireValue(args, ref index, "--classroom");
                    break;
                case "--update-url":
                    updateUrl = RequireValue(args, ref index, "--update-url");
                    if (!Uri.TryCreate(updateUrl, UriKind.Absolute, out var uri) ||
                        uri.Scheme != Uri.UriSchemeHttps)
                        throw new ArgumentException("--update-url wajib URL HTTPS absolut.");
                    break;
                default:
                    throw new ArgumentException($"Argumen installer '{args[index]}' tidak dikenal.");
            }
        }

        if (purgeSecret && !uninstall)
            throw new ArgumentException("--purge-secret hanya boleh dipakai bersama --uninstall.");

        return new InstallerOptions(
            component,
            uninstall,
            purgeSecret,
            silent,
            updatesEnabled,
            bundle is null ? null : Path.GetFullPath(bundle),
            classroom,
            updateUrl);
    }

    private static string RequireValue(string[] args, ref int index, string key)
    {
        if (++index >= args.Length || string.IsNullOrWhiteSpace(args[index]))
            throw new ArgumentException($"Nilai {key} belum diberikan.");
        return args[index];
    }
}
