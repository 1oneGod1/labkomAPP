using System.IO.Compression;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using LabKom.Shared.Updates;

namespace LabKom.Updater;

internal sealed class UpdateEngine(UpdateLogger logger)
{
    private const long MaximumPackageBytes = 2L * 1024 * 1024 * 1024;
    private const long MaximumExtractedBytes = 4L * 1024 * 1024 * 1024;
    private readonly UpdateLogger _logger = logger;
    private readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromMinutes(15),
    };

    public async Task<int> CheckAndApplyAsync(UpdateOptions options)
    {
        EnsureSafeInstallDirectory(options);

        var interrupted = UpdateHealthReporter.ReadPending(options.Component);
        if (interrupted is not null)
        {
            _logger.Error($"Update {interrupted.Version} tidak pernah sehat; rollback otomatis dijalankan.");
            RestoreInterrupted(options, interrupted);
            return 30;
        }

        SignedUpdateManifest manifest;
        try
        {
            manifest = await LoadManifestAsync(options);
            manifest.Validate(options.Component, requireHttps: !options.AllowLocalFiles);
            using var certificate = new X509Certificate2(options.PublicKeyPath!);
            using var publicKey = certificate.GetRSAPublicKey()
                ?? throw new CryptographicException("Sertifikat update tidak memiliki public key RSA.");
            if (!manifest.VerifySignature(publicKey))
                throw new CryptographicException("Tanda tangan manifest update tidak sah.");
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or JsonException or CryptographicException or InvalidDataException)
        {
            _logger.Error($"Manifest update ditolak: {exception.Message}");
            return 10;
        }

        var current = ReleaseDescriptor.Read(options.InstallDirectory);
        if (!string.Equals(current.Component, options.Component, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Komponen instalasi tidak sesuai dengan updater.");
        if (Version.Parse(manifest.Version) <= Version.Parse(current.Version))
        {
            _logger.Info($"Versi {current.Version} sudah terbaru.");
            return 0;
        }

        if (options.Component == "Teacher" && WindowsComponentControl.IsTeacherRunning())
        {
            _logger.Info("Teacher masih terbuka; update aman ditunda.");
            return 20;
        }

        var parent = Directory.GetParent(options.InstallDirectory)?.FullName
            ?? throw new InvalidOperationException("Folder induk instalasi tidak valid.");
        var name = Path.GetFileName(options.InstallDirectory);
        var token = Guid.NewGuid().ToString("N");
        var packagePath = Path.Combine(parent, $"{name}.package-{token}.zip");
        var stagingPath = Path.Combine(parent, $"{name}.staging-{token}");
        var backupPath = Path.Combine(parent, $"{name}.backup-{current.Version}-{token}");
        var failedPath = Path.Combine(parent, $"{name}.failed-{token}");

        try
        {
            await DownloadPackageAsync(manifest.PackageUrl, packagePath, options.AllowLocalFiles);
            VerifyPackageHash(packagePath, manifest.Sha256);
            ExtractPackage(packagePath, stagingPath);

            var staged = ReleaseDescriptor.Read(stagingPath);
            if (!string.Equals(staged.Component, options.Component, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(staged.Version, manifest.Version, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Identitas release.json tidak sama dengan manifest bertanda tangan.");

            WindowsComponentControl.Stop(options);
            Directory.Move(options.InstallDirectory, backupPath);
            try
            {
                var pending = new PendingUpdate(
                    options.Component,
                    manifest.Version,
                    options.InstallDirectory,
                    backupPath,
                    DateTimeOffset.UtcNow);
                UpdateHealthReporter.WritePending(pending);
                Directory.Move(stagingPath, options.InstallDirectory);
                WindowsComponentControl.Start(options);
                if (!WaitForHealth(options, manifest.Version))
                    throw new InvalidOperationException("Versi baru gagal health check.");

                var previousRollback = LoadRollbackRecord(options.Component);
                SaveRollbackRecord(options.Component, new RollbackRecord(
                    options.Component,
                    manifest.Version,
                    current.Version,
                    options.InstallDirectory,
                    backupPath,
                    DateTimeOffset.UtcNow));
                CleanupPreviousBackup(previousRollback, backupPath);
                _logger.Info($"Update {options.Component} {current.Version} -> {manifest.Version} berhasil.");
                return 0;
            }
            catch
            {
                RestoreAfterFailedUpdate(options, backupPath, failedPath);
                throw;
            }
        }
        catch (UpdateDeferredException exception)
        {
            _logger.Info(exception.Message);
            return 20;
        }
        catch (Exception exception)
        {
            _logger.Error($"Update dibatalkan: {exception.Message}");
            return 1;
        }
        finally
        {
            SafeDeleteFile(packagePath, parent, $"{name}.package-");
            SafeDeleteDirectory(stagingPath, parent, $"{name}.staging-");
            SafeDeleteDirectory(failedPath, parent, $"{name}.failed-");
        }
    }

    public Task<int> RollbackAsync(UpdateOptions options)
    {
        EnsureSafeInstallDirectory(options);
        var record = LoadRollbackRecord(options.Component);
        if (record is null)
        {
            _logger.Info("Tidak ada versi sebelumnya yang dapat dipulihkan.");
            return Task.FromResult(0);
        }

        ValidateRecordedPath(record.InstallDirectory, options.InstallDirectory);
        var parent = Directory.GetParent(options.InstallDirectory)?.FullName
            ?? throw new InvalidOperationException("Folder induk instalasi tidak valid.");
        ValidateSibling(record.BackupDirectory, parent, Path.GetFileName(options.InstallDirectory) + ".backup-");
        if (!Directory.Exists(record.BackupDirectory))
            throw new DirectoryNotFoundException("Backup rollback tidak ditemukan.");

        var failedPath = Path.Combine(
            parent,
            $"{Path.GetFileName(options.InstallDirectory)}.failed-{Guid.NewGuid():N}");
        WindowsComponentControl.Stop(options);
        Directory.Move(options.InstallDirectory, failedPath);
        try
        {
            Directory.Move(record.BackupDirectory, options.InstallDirectory);
            WindowsComponentControl.Start(options);
            if (options.Component == "Teacher" &&
                !WindowsComponentControl.RunTeacherHealthCheck(options.InstallDirectory, TimeSpan.FromSeconds(30)))
                throw new InvalidOperationException("Versi rollback gagal health check.");

            SafeDeleteDirectory(
                failedPath,
                parent,
                Path.GetFileName(options.InstallDirectory) + ".failed-");
            DeleteRollbackRecord(options.Component);
            _logger.Info($"Rollback ke versi {record.PreviousVersion} berhasil.");
            return Task.FromResult(0);
        }
        catch
        {
            if (Directory.Exists(options.InstallDirectory))
                Directory.Move(options.InstallDirectory, record.BackupDirectory);
            if (Directory.Exists(failedPath))
                Directory.Move(failedPath, options.InstallDirectory);
            WindowsComponentControl.Start(options);
            throw;
        }
    }

    private async Task<SignedUpdateManifest> LoadManifestAsync(UpdateOptions options)
    {
        string json;
        if (options.AllowLocalFiles && File.Exists(options.ManifestUrl))
        {
            json = await File.ReadAllTextAsync(Path.GetFullPath(options.ManifestUrl));
        }
        else
        {
            using var response = await _httpClient.GetAsync(options.ManifestUrl);
            response.EnsureSuccessStatusCode();
            json = await response.Content.ReadAsStringAsync();
        }

        return JsonSerializer.Deserialize<SignedUpdateManifest>(
            json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("Manifest update kosong.");
    }

    private async Task DownloadPackageAsync(string location, string target, bool allowLocal)
    {
        if (allowLocal && Uri.TryCreate(location, UriKind.Absolute, out var uri) && uri.IsFile)
        {
            File.Copy(uri.LocalPath, target, overwrite: false);
            return;
        }

        if (allowLocal && File.Exists(location))
        {
            File.Copy(Path.GetFullPath(location), target, overwrite: false);
            return;
        }

        using var response = await _httpClient.GetAsync(location, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > MaximumPackageBytes)
            throw new InvalidDataException("Paket update melebihi batas ukuran.");

        await using var input = await response.Content.ReadAsStreamAsync();
        await using var output = new FileStream(
            target,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            1024 * 128,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
        var buffer = new byte[1024 * 128];
        long total = 0;
        while (true)
        {
            var read = await input.ReadAsync(buffer);
            if (read == 0) break;
            total += read;
            if (total > MaximumPackageBytes)
                throw new InvalidDataException("Paket update melebihi batas ukuran.");
            await output.WriteAsync(buffer.AsMemory(0, read));
        }
        await output.FlushAsync();
    }

    private static void VerifyPackageHash(string path, string expected)
    {
        using var stream = File.OpenRead(path);
        var actual = Convert.ToHexString(SHA256.HashData(stream));
        var left = Convert.FromHexString(actual);
        var right = Convert.FromHexString(expected);
        if (!CryptographicOperations.FixedTimeEquals(left, right))
            throw new CryptographicException("SHA-256 paket update tidak cocok.");
    }

    private static void ExtractPackage(string packagePath, string stagingPath)
    {
        Directory.CreateDirectory(stagingPath);
        var root = Path.GetFullPath(stagingPath) + Path.DirectorySeparatorChar;
        long total = 0;

        using var archive = ZipFile.OpenRead(packagePath);
        foreach (var entry in archive.Entries)
        {
            if (((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
                throw new InvalidDataException("Paket update tidak boleh berisi symbolic link.");

            total += entry.Length;
            if (total > MaximumExtractedBytes)
                throw new InvalidDataException("Isi paket update melebihi batas ukuran.");

            var relative = entry.FullName
                .Replace('/', Path.DirectorySeparatorChar)
                .Replace('\\', Path.DirectorySeparatorChar);
            var destination = Path.GetFullPath(Path.Combine(stagingPath, relative));
            if (!destination.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Paket update mencoba menulis di luar staging.");

            if (entry.FullName.EndsWith("/", StringComparison.Ordinal) ||
                entry.FullName.EndsWith("\\", StringComparison.Ordinal))
            {
                Directory.CreateDirectory(destination);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            using var input = entry.Open();
            using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            input.CopyTo(output);
            File.SetLastWriteTimeUtc(destination, entry.LastWriteTime.UtcDateTime);
        }
    }

    private bool WaitForHealth(UpdateOptions options, string version)
    {
        if (options.Component == "Teacher")
            return WindowsComponentControl.RunTeacherHealthCheck(
                options.InstallDirectory,
                TimeSpan.FromSeconds(30)) &&
                   !File.Exists(UpdateHealthReporter.GetPendingPath(options.Component));

        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            if (!File.Exists(UpdateHealthReporter.GetPendingPath(options.Component)))
            {
                Thread.Sleep(3_000);
                return WindowsComponentControl.IsServiceRunning(options.ServiceName!);
            }

            Thread.Sleep(500);
        }
        return false;
    }

    private void RestoreInterrupted(UpdateOptions options, PendingUpdate pending)
    {
        ValidateRecordedPath(pending.InstallDirectory, options.InstallDirectory);
        var parent = Directory.GetParent(options.InstallDirectory)?.FullName
            ?? throw new InvalidOperationException("Folder induk instalasi tidak valid.");
        ValidateSibling(
            pending.BackupDirectory,
            parent,
            Path.GetFileName(options.InstallDirectory) + ".backup-");

        WindowsComponentControl.Stop(options);
        var failed = Path.Combine(
            parent,
            $"{Path.GetFileName(options.InstallDirectory)}.failed-{Guid.NewGuid():N}");
        if (Directory.Exists(options.InstallDirectory))
            Directory.Move(options.InstallDirectory, failed);
        Directory.Move(pending.BackupDirectory, options.InstallDirectory);
        File.Delete(UpdateHealthReporter.GetPendingPath(options.Component));
        WindowsComponentControl.Start(options);
        SafeDeleteDirectory(failed, parent, Path.GetFileName(options.InstallDirectory) + ".failed-");
    }

    private void RestoreAfterFailedUpdate(UpdateOptions options, string backupPath, string failedPath)
    {
        try
        {
            WindowsComponentControl.Stop(options);
        }
        catch (Exception exception)
        {
            _logger.Error($"Gagal menghentikan versi rusak saat rollback: {exception.Message}");
        }

        if (Directory.Exists(options.InstallDirectory))
            Directory.Move(options.InstallDirectory, failedPath);
        if (Directory.Exists(backupPath))
            Directory.Move(backupPath, options.InstallDirectory);

        var pendingPath = UpdateHealthReporter.GetPendingPath(options.Component);
        if (File.Exists(pendingPath)) File.Delete(pendingPath);
        WindowsComponentControl.Start(options);
    }

    private static void EnsureSafeInstallDirectory(UpdateOptions options)
    {
        if (options.AllowLocalFiles) return;
        var root = Path.GetFullPath(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "LabKom")) + Path.DirectorySeparatorChar;
        var install = Path.GetFullPath(options.InstallDirectory);
        if (!install.StartsWith(root, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(install, root.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Folder instalasi harus berada di bawah Program Files\\LabKom.");
    }

    private static void ValidateRecordedPath(string recorded, string expected)
    {
        if (!string.Equals(
                Path.GetFullPath(recorded).TrimEnd(Path.DirectorySeparatorChar),
                Path.GetFullPath(expected).TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Path rollback tidak sesuai dengan instalasi.");
    }

    private static void ValidateSibling(string path, string parent, string requiredPrefix)
    {
        var fullParent = Path.GetFullPath(parent) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(path);
        if (!fullPath.StartsWith(fullParent, StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFileName(fullPath).StartsWith(requiredPrefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Path sementara update tidak aman.");
    }

    private static string RollbackPath(string component) => Path.Combine(
        UpdateHealthReporter.StateDirectory,
        $"rollback-{component.ToLowerInvariant()}.json");

    private static RollbackRecord? LoadRollbackRecord(string component)
    {
        var path = RollbackPath(component);
        return File.Exists(path)
            ? JsonSerializer.Deserialize<RollbackRecord>(File.ReadAllText(path))
            : null;
    }

    private static void SaveRollbackRecord(string component, RollbackRecord record)
    {
        Directory.CreateDirectory(UpdateHealthReporter.StateDirectory);
        File.WriteAllText(
            RollbackPath(component),
            JsonSerializer.Serialize(record, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void DeleteRollbackRecord(string component)
    {
        var path = RollbackPath(component);
        if (File.Exists(path)) File.Delete(path);
    }

    private static void CleanupPreviousBackup(RollbackRecord? record, string currentBackup)
    {
        if (record is null ||
            string.Equals(record.BackupDirectory, currentBackup, StringComparison.OrdinalIgnoreCase))
            return;

        var parent = Directory.GetParent(record.InstallDirectory)?.FullName;
        if (parent is null) return;
        SafeDeleteDirectory(
            record.BackupDirectory,
            parent,
            Path.GetFileName(record.InstallDirectory) + ".backup-");
    }

    private static void SafeDeleteFile(string path, string parent, string prefix)
    {
        if (!File.Exists(path)) return;
        ValidateSibling(path, parent, prefix);
        File.Delete(path);
    }

    private static void SafeDeleteDirectory(string path, string parent, string prefix)
    {
        if (!Directory.Exists(path)) return;
        ValidateSibling(path, parent, prefix);
        Directory.Delete(path, recursive: true);
    }

    private sealed record RollbackRecord(
        string Component,
        string InstalledVersion,
        string PreviousVersion,
        string InstallDirectory,
        string BackupDirectory,
        DateTimeOffset CreatedAtUtc);
}
