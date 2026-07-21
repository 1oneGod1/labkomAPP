using System.IO.Compression;
using System.Reflection;

namespace LabKom.Installer;

internal static class PayloadExtractor
{
    public static void Extract(string destination)
    {
        var resource = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("LabKom.Installer.Payload.zip")
            ?? throw new InvalidOperationException("Payload installer tidak ditemukan.");

        Directory.CreateDirectory(destination);
        var root = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;
        using (resource)
        using (var archive = new ZipArchive(resource, ZipArchiveMode.Read))
        {
            foreach (var entry in archive.Entries)
            {
                if (((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
                    throw new InvalidDataException("Payload installer mengandung symbolic link.");

                var relative = entry.FullName
                    .Replace('/', Path.DirectorySeparatorChar)
                    .Replace('\\', Path.DirectorySeparatorChar);
                var target = Path.GetFullPath(Path.Combine(destination, relative));
                if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Payload mencoba keluar dari staging.");

                if (entry.FullName.EndsWith("/", StringComparison.Ordinal) ||
                    entry.FullName.EndsWith("\\", StringComparison.Ordinal))
                {
                    Directory.CreateDirectory(target);
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                using var input = entry.Open();
                using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                input.CopyTo(output);
            }
        }

        if (!Directory.Exists(Path.Combine(destination, "component")) ||
            !File.Exists(Path.Combine(destination, "updater", "LabKom.Updater.exe")) ||
            !File.Exists(Path.Combine(destination, "update-public.cer")))
            throw new InvalidDataException("Struktur payload installer tidak lengkap.");
    }
}
