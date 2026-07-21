using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Runtime.Versioning;
using System.Text.Json;

namespace LabKom.Shared.Security;

/// <summary>
/// Stores the classroom shared secret encrypted with Windows DPAPI (LocalMachine).
/// The encrypted file is readable by interactive users because the Student Desktop
/// needs it, but only Administrators and SYSTEM may replace it.
/// </summary>
[SupportedOSPlatform("windows")]
public static class ProvisionedSecretStore
{
    public const int SchemaVersion = 1;
    public const string EnvironmentVariableName = "LABKOM_SHARED_SECRET";

    private static readonly byte[] Entropy =
        "LabKom.ClassroomSecret.v1"u8.ToArray();

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "LabKom",
        "Security",
        "classroom.secret");

    public static ProvisionedSecretRecord Create(string? classroomName = null)
    {
        var random = RandomNumberGenerator.GetBytes(48);
        try
        {
            return new ProvisionedSecretRecord(
                SchemaVersion,
                Guid.NewGuid().ToString("N"),
                string.IsNullOrWhiteSpace(classroomName) ? Environment.MachineName : classroomName.Trim(),
                DateTimeOffset.UtcNow,
                Convert.ToBase64String(random));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(random);
        }
    }

    public static string? Resolve(string? configuredSecret, string? path = null)
    {
        var environmentSecret = Environment.GetEnvironmentVariable(EnvironmentVariableName);
        if (environmentSecret is not null) return environmentSecret;
        if (!string.IsNullOrWhiteSpace(configuredSecret)) return configuredSecret;

        return TryRead(path, out var record) ? record.Secret : null;
    }

    public static void Write(ProvisionedSecretRecord record, string? path = null)
    {
        ArgumentNullException.ThrowIfNull(record);
        Validate(record);
        EnsureWindows();

        var targetPath = Path.GetFullPath(path ?? DefaultPath);
        var directory = Path.GetDirectoryName(targetPath)
            ?? throw new InvalidOperationException("Folder penyimpanan secret tidak valid.");
        Directory.CreateDirectory(directory);

        var clear = JsonSerializer.SerializeToUtf8Bytes(record, JsonOptions);
        byte[]? encrypted = null;
        var temporaryPath = targetPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            encrypted = ProtectedData.Protect(clear, Entropy, DataProtectionScope.LocalMachine);
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       4096,
                       FileOptions.WriteThrough))
            {
                stream.Write("LKS1"u8);
                stream.Write(encrypted);
                stream.Flush(flushToDisk: true);
            }

            ApplyAcl(temporaryPath);
            File.Move(temporaryPath, targetPath, overwrite: true);
            ApplyAcl(targetPath);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(clear);
            if (encrypted is not null) CryptographicOperations.ZeroMemory(encrypted);
            TryDelete(temporaryPath);
        }
    }

    public static ProvisionedSecretRecord Read(string? path = null)
    {
        if (!TryRead(path, out var record))
        {
            throw new FileNotFoundException(
                "Secret LabKom belum diprovisioning.",
                Path.GetFullPath(path ?? DefaultPath));
        }

        return record;
    }

    public static bool TryRead(
        string? path,
        out ProvisionedSecretRecord record)
    {
        record = null!;
        EnsureWindows();

        var targetPath = Path.GetFullPath(path ?? DefaultPath);
        if (!File.Exists(targetPath)) return false;

        var file = File.ReadAllBytes(targetPath);
        byte[]? clear = null;
        try
        {
            if (file.Length <= 4 || !file.AsSpan(0, 4).SequenceEqual("LKS1"u8))
            {
                throw new InvalidDataException("Format secret LabKom tidak dikenali.");
            }

            clear = ProtectedData.Unprotect(
                file.AsSpan(4).ToArray(),
                Entropy,
                DataProtectionScope.LocalMachine);
            record = JsonSerializer.Deserialize<ProvisionedSecretRecord>(clear, JsonOptions)
                ?? throw new InvalidDataException("Isi secret LabKom tidak valid.");
            Validate(record);
            return true;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(file);
            if (clear is not null) CryptographicOperations.ZeroMemory(clear);
        }
    }

    public static void ExportProvisioningBundle(
        ProvisionedSecretRecord record,
        string outputPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        Validate(record);

        var fullPath = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException("Folder bundle provisioning tidak valid."));
        var temporaryPath = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var json = JsonSerializer.SerializeToUtf8Bytes(record, JsonOptions);
        try
        {
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       4096,
                       FileOptions.WriteThrough))
            {
                stream.Write(json);
                stream.Flush(flushToDisk: true);
            }

            ApplyPrivateBundleAcl(temporaryPath);
            File.Move(temporaryPath, fullPath, overwrite: true);
            ApplyPrivateBundleAcl(fullPath);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(json);
            TryDelete(temporaryPath);
        }
    }

    public static ProvisionedSecretRecord ImportProvisioningBundle(string bundlePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bundlePath);
        var record = JsonSerializer.Deserialize<ProvisionedSecretRecord>(
            File.ReadAllText(Path.GetFullPath(bundlePath)),
            JsonOptions) ?? throw new InvalidDataException("Bundle provisioning kosong atau rusak.");
        Validate(record);
        return record;
    }

    private static void Validate(ProvisionedSecretRecord record)
    {
        if (record.SchemaVersion != SchemaVersion)
            throw new InvalidDataException($"Schema provisioning {record.SchemaVersion} tidak didukung.");
        if (!Guid.TryParseExact(record.ClassroomId, "N", out _))
            throw new InvalidDataException("ClassroomId provisioning tidak valid.");
        if (string.IsNullOrWhiteSpace(record.ClassroomName) || record.ClassroomName.Length > 128)
            throw new InvalidDataException("Nama kelas provisioning tidak valid.");
        if (string.IsNullOrWhiteSpace(record.Secret) || record.Secret.Length < 32)
            throw new InvalidDataException("Secret provisioning terlalu pendek.");
    }

    public static void RestrictToAdministrators(string? path = null)
    {
        EnsureWindows();
        var target = Path.GetFullPath(path ?? DefaultPath);
        if (!File.Exists(target)) return;
        var security = new FileSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            FileSystemRights.FullControl,
            AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            FileSystemRights.FullControl,
            AccessControlType.Allow));
        new FileInfo(target).SetAccessControl(security);
    }

    private static void ApplyAcl(string path)
    {
        var security = new FileSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            FileSystemRights.FullControl,
            AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            FileSystemRights.FullControl,
            AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
            FileSystemRights.ReadAndExecute,
            AccessControlType.Allow));
        new FileInfo(path).SetAccessControl(security);
    }

    private static void ApplyPrivateBundleAcl(string path)
    {
        var security = new FileSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            FileSystemRights.FullControl,
            AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            FileSystemRights.FullControl,
            AccessControlType.Allow));

        var currentUser = WindowsIdentity.GetCurrent().User;
        if (currentUser is not null)
        {
            security.AddAccessRule(new FileSystemAccessRule(
                currentUser,
                FileSystemRights.FullControl,
                AccessControlType.Allow));
        }

        new FileInfo(path).SetAccessControl(security);
    }

    private static void EnsureWindows()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Provisioning secret LabKom hanya tersedia di Windows.");
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException)
        {
            // A later installation can clean a stale uniquely named temporary file.
        }
        catch (UnauthorizedAccessException)
        {
            // Best effort cleanup only.
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };
}

public sealed record ProvisionedSecretRecord(
    int SchemaVersion,
    string ClassroomId,
    string ClassroomName,
    DateTimeOffset CreatedAtUtc,
    string Secret);
