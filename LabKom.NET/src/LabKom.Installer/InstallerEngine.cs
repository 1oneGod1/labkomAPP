using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using LabKom.Shared.Hub;
using LabKom.Shared.Security;

namespace LabKom.Installer;

internal sealed class InstallerEngine(InstallerLogger logger)
{
    private readonly InstallerLogger _logger = logger;

    public int Install(InstallerOptions options)
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "LabKom");
        Directory.CreateDirectory(root);

        var token = Guid.NewGuid().ToString("N");
        var staging = Path.Combine(root, $".install-{options.Component.ToLowerInvariant()}-{token}");
        var target = Path.Combine(root, options.Component);
        var updaterTarget = Path.Combine(root, "Updater");
        var targetBackup = Path.Combine(root, $".backup-install-{options.Component.ToLowerInvariant()}-{token}");
        var updaterBackup = Path.Combine(root, $".backup-installer-updater-{token}");

        var hadSecret = ProvisionedSecretStore.TryRead(null, out var previousSecret);
        string? previousVersion = null;
        var previousInstalled = Directory.Exists(target);
        var updaterInstalled = Directory.Exists(updaterTarget);

        try
        {
            _logger.Info($"Memasang LabKom {options.Component}...");
            PayloadExtractor.Extract(staging);
            var componentPayload = Path.Combine(staging, "component");
            var updaterPayload = Path.Combine(staging, "updater");
            File.Copy(
                Path.Combine(staging, "update-public.cer"),
                Path.Combine(updaterPayload, "update-public.cer"),
                overwrite: true);

            var release = ReadRelease(componentPayload);
            if (!string.Equals(release.Component, options.Component, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Payload installer salah komponen.");
            previousVersion = previousInstalled ? TryReadReleaseVersion(target) ?? "0.0.0" : null;

            WindowsRegistration.PrepareForInstall();
            if (previousInstalled)
            {
                WindowsRegistration.Unregister(options.Component, _logger);
                Directory.Move(target, targetBackup);
            }
            if (updaterInstalled) Directory.Move(updaterTarget, updaterBackup);

            try
            {
                Directory.Move(componentPayload, target);
                Directory.Move(updaterPayload, updaterTarget);
                var provisioning = Provision(options);
                WindowsRegistration.Register(options, root, release.Version, _logger);

                if (options.Component == "Teacher" &&
                    !WindowsRegistration.RunTeacherHealthCheck(target))
                    throw new InvalidOperationException("Teacher gagal health check setelah instalasi.");

                DeleteDirectoryIfExists(targetBackup);
                DeleteDirectoryIfExists(updaterBackup);
                if (provisioning.ExportedBundlePath is not null)
                {
                    _logger.Info($"Bundle provisioning siswa: {provisioning.ExportedBundlePath}");
                    _logger.Info("Simpan bundle itu secara offline; jangan commit atau kirim lewat chat.");
                }
                _logger.Info($"LabKom {options.Component} {release.Version} berhasil dipasang.");
                return 0;
            }
            catch
            {
                RollbackInstall(
                    options,
                    root,
                    target,
                    updaterTarget,
                    targetBackup,
                    updaterBackup,
                    previousInstalled,
                    updaterInstalled,
                    previousVersion,
                    hadSecret,
                    previousSecret);
                throw;
            }
        }
        catch (Exception exception)
        {
            _logger.Error($"Instalasi gagal dan perubahan telah di-rollback: {exception.Message}");
            return 1;
        }
        finally
        {
            DeleteDirectoryIfExists(staging);
        }
    }

    public int Uninstall(InstallerOptions options)
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "LabKom");
        var target = Path.Combine(root, options.Component);

        WindowsRegistration.PrepareForInstall();
        WindowsRegistration.Unregister(options.Component, _logger);
        DeleteDirectoryIfExists(target);

        var otherComponent = options.Component == "Student" ? "Teacher" : "Student";
        if (!WindowsRegistration.IsInstalled(otherComponent))
            DeleteDirectoryIfExists(Path.Combine(root, "Updater"));

        if (options.PurgeSecret)
        {
            var secret = ProvisionedSecretStore.DefaultPath;
            if (File.Exists(secret)) File.Delete(secret);
            _logger.Info("Secret kelas lokal ikut dihapus (--purge-secret).");
        }
        else
        {
            _logger.Info("Secret kelas dipertahankan agar reinstall/upgrade tetap dapat tersambung.");
        }

        ScheduleCachedInstallerDeletion(options.Component);
        _logger.Info($"LabKom {options.Component} berhasil dihapus.");
        return 0;
    }

    private ProvisioningResult Provision(InstallerOptions options)
    {
        ProvisionedSecretRecord record;
        var created = false;
        if (options.BundlePath is not null)
        {
            if (!File.Exists(options.BundlePath))
                throw new FileNotFoundException("Bundle provisioning tidak ditemukan.", options.BundlePath);
            record = ProvisionedSecretStore.ImportProvisioningBundle(options.BundlePath);
        }
        else if (ProvisionedSecretStore.TryRead(null, out var existing))
        {
            record = existing;
        }
        else
        {
            var environmentSecret = Environment.GetEnvironmentVariable(
                ProvisionedSecretStore.EnvironmentVariableName);
            if (HubSecurity.IsStrongSecret(environmentSecret))
            {
                record = CreateRecordFromExistingSecret(
                    environmentSecret!,
                    options.ClassroomName);
                created = options.Component == "Teacher";
            }
            else if (environmentSecret is not null)
            {
                throw new InvalidOperationException(
                    $"Environment variable {ProvisionedSecretStore.EnvironmentVariableName} ada tetapi terlalu pendek.");
            }
            else if (options.Component == "Student")
            {
                throw new InvalidOperationException(
                    "Installer Student pertama kali memerlukan --bundle <file.provision.json> dari PC Teacher.");
            }
            else
            {
                record = ProvisionedSecretStore.Create(options.ClassroomName);
                created = true;
            }
        }

        ProvisionedSecretStore.Write(record);
        if (!created) return new ProvisioningResult(record, null);

        var documents = Environment.GetFolderPath(Environment.SpecialFolder.CommonDocuments);
        var bundleDirectory = Path.Combine(documents, "LabKom", "Provisioning");
        var bundlePath = Path.Combine(
            bundleDirectory,
            $"LabKom-{SanitizeFileName(record.ClassroomName)}-{record.ClassroomId[..8]}.provision.json");
        ProvisionedSecretStore.ExportProvisioningBundle(record, bundlePath);
        return new ProvisioningResult(record, bundlePath);
    }

    private static ProvisionedSecretRecord CreateRecordFromExistingSecret(
        string secret,
        string? classroomName)
    {
        var hash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(secret));
        var identifier = hash.AsSpan(0, 16).ToArray();
        identifier[7] = (byte)((identifier[7] & 0x0F) | 0x40);
        identifier[8] = (byte)((identifier[8] & 0x3F) | 0x80);
        try
        {
            return new ProvisionedSecretRecord(
                ProvisionedSecretStore.SchemaVersion,
                new Guid(identifier).ToString("N"),
                string.IsNullOrWhiteSpace(classroomName) ? "LabKom Migrated" : classroomName.Trim(),
                DateTimeOffset.UtcNow,
                secret);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(hash);
            CryptographicOperations.ZeroMemory(identifier);
        }
    }

    private void RollbackInstall(
        InstallerOptions options,
        string root,
        string target,
        string updaterTarget,
        string targetBackup,
        string updaterBackup,
        bool previousInstalled,
        bool updaterInstalled,
        string? previousVersion,
        bool hadSecret,
        ProvisionedSecretRecord previousSecret)
    {
        try
        {
            WindowsRegistration.Unregister(options.Component, _logger);
        }
        catch (Exception exception)
        {
            _logger.Error($"Pembersihan registrasi versi gagal: {exception.Message}");
        }

        DeleteDirectoryIfExists(target);
        DeleteDirectoryIfExists(updaterTarget);
        if (previousInstalled && Directory.Exists(targetBackup))
            Directory.Move(targetBackup, target);
        if (updaterInstalled && Directory.Exists(updaterBackup))
            Directory.Move(updaterBackup, updaterTarget);

        if (hadSecret)
        {
            ProvisionedSecretStore.Write(previousSecret);
        }
        else if (File.Exists(ProvisionedSecretStore.DefaultPath))
        {
            File.Delete(ProvisionedSecretStore.DefaultPath);
        }

        if (previousInstalled && previousVersion is not null)
            WindowsRegistration.Register(options, root, previousVersion, _logger);
    }

    private static ReleaseInfo ReadRelease(string directory)
    {
        var path = Path.Combine(directory, "release.json");
        if (!File.Exists(path))
            throw new InvalidDataException("Payload tidak memiliki release.json.");
        var value = JsonSerializer.Deserialize<ReleaseInfo>(
            File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("release.json installer rusak.");
        if (value.SchemaVersion != 1 ||
            !Version.TryParse(value.Version, out _) ||
            value.Component is not ("Student" or "Teacher"))
            throw new InvalidDataException("release.json installer tidak valid.");
        return value;
    }

    private static string? TryReadReleaseVersion(string directory)
    {
        try
        {
            return ReadRelease(directory).Version;
        }
        catch (Exception exception) when (exception is IOException or JsonException)
        {
            return null;
        }
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var filtered = new string(value
            .Select(character => invalid.Contains(character) ? '-' : character)
            .ToArray());
        return string.IsNullOrWhiteSpace(filtered) ? "Kelas" : filtered;
    }

    private static void DeleteDirectoryIfExists(string path)
    {
        if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
    }

    private static void ScheduleCachedInstallerDeletion(string component)
    {
        var cached = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "LabKom",
            "Installer",
            "Cache",
            $"LabKom-{component}-Setup.exe");
        if (File.Exists(cached))
            _ = MoveFileEx(cached, null, MoveFileFlags.DelayUntilReboot);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MoveFileEx(
        string existingFileName,
        string? newFileName,
        MoveFileFlags flags);

    [Flags]
    private enum MoveFileFlags : uint
    {
        DelayUntilReboot = 0x4,
    }

    private sealed record ReleaseInfo(int SchemaVersion, string Component, string Version);
    private sealed record ProvisioningResult(
        ProvisionedSecretRecord Record,
        string? ExportedBundlePath);
}
