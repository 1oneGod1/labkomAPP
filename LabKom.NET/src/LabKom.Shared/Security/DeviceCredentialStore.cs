using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using LabKom.Shared.Hub;

namespace LabKom.Shared.Security;

[SupportedOSPlatform("windows")]
public static class DeviceCredentialStore
{
    public const int SchemaVersion = 1;
    public const int DefaultKeyVersion = 1;
    private static readonly byte[] Entropy = "LabKom.DeviceCredential.v1"u8.ToArray();

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "LabKom",
        "Security",
        "device.credential");

    public static DeviceCredentialRecord EnsureFromProvisioning(
        ProvisionedSecretRecord provisioning,
        string pcName,
        int keyVersion = DefaultKeyVersion,
        string? path = null)
    {
        if (TryRead(out var existing, path)
            && existing is not null
            && string.Equals(existing.ClassroomId, provisioning.ClassroomId, StringComparison.Ordinal)
            && string.Equals(existing.PcName, pcName, StringComparison.OrdinalIgnoreCase)
            && existing.KeyVersion >= keyVersion)
        {
            return existing;
        }

        var deviceId = existing is not null
                       && string.Equals(existing.ClassroomId, provisioning.ClassroomId, StringComparison.Ordinal)
            ? existing.DeviceId
            : Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow;
        var secret = DeriveSecret(
            provisioning.Secret,
            provisioning.ClassroomId,
            deviceId,
            pcName,
            keyVersion);
        var record = new DeviceCredentialRecord(
            SchemaVersion,
            provisioning.ClassroomId,
            deviceId,
            pcName,
            keyVersion,
            CredentialId(secret),
            now,
            secret);
        Write(record, path);
        return record;
    }

    public static DeviceCredentialRecord RotateFromProvisioning(
        ProvisionedSecretRecord provisioning,
        int keyVersion,
        string? path = null)
    {
        var existing = Read(path);
        if (!string.Equals(existing.ClassroomId, provisioning.ClassroomId, StringComparison.Ordinal))
            throw new InvalidDataException("Credential perangkat berasal dari kelas lain.");
        if (keyVersion <= existing.KeyVersion)
            return existing;

        return EnsureFromProvisioning(
            provisioning,
            existing.PcName,
            keyVersion,
            path);
    }

    public static DeviceAuthenticationMaterial Resolve(string? legacySecret, string? path = null) =>
        TryRead(out var credential, path) && credential is not null
            ? new DeviceAuthenticationMaterial(
                credential.Secret,
                credential.DeviceId,
                credential.KeyVersion,
                credential.PcName,
                IsLegacy: false)
            : new DeviceAuthenticationMaterial(
                legacySecret ?? string.Empty,
                DeviceId: null,
                KeyVersion: null,
                Environment.MachineName,
                IsLegacy: true);

    public static string DeriveSecret(
        string rootSecret,
        string classroomId,
        string deviceId,
        string pcName,
        int keyVersion)
    {
        if (!HubSecurity.IsStrongSecret(rootSecret))
            throw new ArgumentException("Root secret terlalu pendek.", nameof(rootSecret));
        if (!Guid.TryParseExact(classroomId, "N", out _)
            || !Guid.TryParseExact(deviceId, "N", out _)
            || !HubSecurity.IsValidPcName(pcName)
            || keyVersion <= 0)
            throw new ArgumentException("Parameter derivasi credential perangkat tidak valid.");

        var key = Encoding.UTF8.GetBytes(rootSecret);
        var payload = Encoding.UTF8.GetBytes(
            $"LabKom.Device.v1\n{classroomId}\n{deviceId}\n{pcName.ToLowerInvariant()}\n{keyVersion}");
        try
        {
            return Convert.ToBase64String(HMACSHA256.HashData(key, payload));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(payload);
        }
    }

    public static bool TryRead(out DeviceCredentialRecord? record, string? path = null)
    {
        EnsureWindows();
        record = null;
        var target = Path.GetFullPath(path ?? DefaultPath);
        if (!File.Exists(target)) return false;

        var bytes = File.ReadAllBytes(target);
        byte[]? clear = null;
        try
        {
            if (bytes.Length <= 4 || !bytes.AsSpan(0, 4).SequenceEqual("LDC1"u8))
                throw new InvalidDataException("Format credential perangkat tidak dikenali.");
            clear = ProtectedData.Unprotect(
                bytes.AsSpan(4).ToArray(),
                Entropy,
                DataProtectionScope.LocalMachine);
            record = JsonSerializer.Deserialize<DeviceCredentialRecord>(clear, JsonOptions)
                     ?? throw new InvalidDataException("Credential perangkat kosong.");
            Validate(record);
            return true;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
            if (clear is not null) CryptographicOperations.ZeroMemory(clear);
        }
    }

    public static DeviceCredentialRecord Read(string? path = null) =>
        TryRead(out var record, path) && record is not null
            ? record
            : throw new FileNotFoundException("Credential perangkat belum dibuat.");

    public static void Write(DeviceCredentialRecord record, string? path = null)
    {
        EnsureWindows();
        Validate(record);
        var target = Path.GetFullPath(path ?? DefaultPath);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var clear = JsonSerializer.SerializeToUtf8Bytes(record, JsonOptions);
        byte[]? encrypted = null;
        try
        {
            encrypted = ProtectedData.Protect(clear, Entropy, DataProtectionScope.LocalMachine);
            using (var stream = new FileStream(
                       temporary,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       4096,
                       FileOptions.WriteThrough))
            {
                stream.Write("LDC1"u8);
                stream.Write(encrypted);
                stream.Flush(flushToDisk: true);
            }

            ApplyAcl(temporary);
            File.Move(temporary, target, overwrite: true);
            ApplyAcl(target);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(clear);
            if (encrypted is not null) CryptographicOperations.ZeroMemory(encrypted);
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static string CredentialId(string secret)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(secret));
        return Convert.ToHexString(hash.AsSpan(0, 16)).ToLowerInvariant();
    }

    private static void Validate(DeviceCredentialRecord record)
    {
        if (record.SchemaVersion != SchemaVersion
            || !Guid.TryParseExact(record.ClassroomId, "N", out _)
            || !Guid.TryParseExact(record.DeviceId, "N", out _)
            || !HubSecurity.IsValidPcName(record.PcName)
            || record.KeyVersion <= 0
            || string.IsNullOrWhiteSpace(record.CredentialId)
            || record.CredentialId.Length != 32
            || !HubSecurity.IsStrongSecret(record.Secret)
            || !string.Equals(
                record.CredentialId,
                CredentialId(record.Secret),
                StringComparison.Ordinal))
            throw new InvalidDataException("Credential perangkat tidak valid.");
    }

    private static void ApplyAcl(string path)
    {
        var security = new FileSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(Rule(WellKnownSidType.LocalSystemSid, FileSystemRights.FullControl));
        security.AddAccessRule(Rule(WellKnownSidType.BuiltinAdministratorsSid, FileSystemRights.FullControl));
        security.AddAccessRule(Rule(WellKnownSidType.BuiltinUsersSid, FileSystemRights.ReadAndExecute));
        new FileInfo(path).SetAccessControl(security);
    }

    private static FileSystemAccessRule Rule(WellKnownSidType sid, FileSystemRights rights) =>
        new(new SecurityIdentifier(sid, null), rights, AccessControlType.Allow);

    private static void EnsureWindows()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Credential perangkat hanya tersedia di Windows.");
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };
}

public sealed record DeviceCredentialRecord(
    int SchemaVersion,
    string ClassroomId,
    string DeviceId,
    string PcName,
    int KeyVersion,
    string CredentialId,
    DateTimeOffset CreatedAtUtc,
    string Secret);

public sealed record DeviceAuthenticationMaterial(
    string Secret,
    string? DeviceId,
    int? KeyVersion,
    string PcName,
    bool IsLegacy);
