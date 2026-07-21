using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;

namespace LabKom.Shared.Security;

/// <summary>
/// Administrator-controlled, time-limited local override for managed Student UI.
/// The file is readable by Student Desktop, but only Administrators and SYSTEM
/// can create, replace, or remove it.
/// </summary>
[SupportedOSPlatform("windows")]
public static class EmergencyUnlockStore
{
    public const int SchemaVersion = 1;
    public const int DefaultDurationMinutes = 15;
    public const int MaximumDurationMinutes = 120;

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "LabKom",
        "Recovery",
        "emergency-unlock.json");

    public static EmergencyUnlockRecord Activate(
        TimeSpan duration,
        string? reason = null,
        string? issuedBy = null,
        string? path = null)
    {
        EnsureWindows();
        if (duration < TimeSpan.FromMinutes(1)
            || duration > TimeSpan.FromMinutes(MaximumDurationMinutes))
        {
            throw new ArgumentOutOfRangeException(
                nameof(duration),
                $"Durasi emergency unlock harus 1-{MaximumDurationMinutes} menit.");
        }

        var now = DateTimeOffset.UtcNow;
        var record = new EmergencyUnlockRecord(
            SchemaVersion,
            Environment.MachineName,
            now,
            now.Add(duration),
            Normalize(issuedBy, 128, Environment.UserName),
            Normalize(reason, 256, "Pemulihan darurat oleh administrator"));
        Write(record, path);
        return record;
    }

    public static bool TryGetActive(
        DateTimeOffset now,
        out EmergencyUnlockRecord record,
        string? path = null)
    {
        record = null!;
        try
        {
            if (!TryRead(out var candidate, path) || !IsActive(candidate, now))
                return false;
            record = candidate;
            return true;
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or JsonException
                or InvalidDataException)
        {
            return false;
        }
    }

    public static bool TryRead(
        out EmergencyUnlockRecord record,
        string? path = null)
    {
        EnsureWindows();
        record = null!;
        var target = Path.GetFullPath(path ?? DefaultPath);
        if (!File.Exists(target)) return false;

        var value = JsonSerializer.Deserialize<EmergencyUnlockRecord>(
            File.ReadAllText(target),
            JsonOptions)
            ?? throw new InvalidDataException("File emergency unlock kosong.");
        Validate(value);
        record = value;
        return true;
    }

    public static bool IsActive(
        EmergencyUnlockRecord record,
        DateTimeOffset now)
    {
        Validate(record);
        return now >= record.IssuedAtUtc.AddMinutes(-1)
               && now < record.ExpiresAtUtc;
    }

    public static void Clear(string? path = null)
    {
        EnsureWindows();
        var target = Path.GetFullPath(path ?? DefaultPath);
        if (File.Exists(target)) File.Delete(target);
    }

    private static void Write(EmergencyUnlockRecord record, string? path)
    {
        Validate(record);
        var target = Path.GetFullPath(path ?? DefaultPath);
        var directory = Path.GetDirectoryName(target)
            ?? throw new InvalidOperationException("Folder emergency unlock tidak valid.");
        Directory.CreateDirectory(directory);
        ApplyDirectoryAcl(directory);

        var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(
                       temporary,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       4096,
                       FileOptions.WriteThrough))
            {
                JsonSerializer.Serialize(stream, record, JsonOptions);
                stream.Flush(flushToDisk: true);
            }

            ApplyFileAcl(temporary);
            File.Move(temporary, target, overwrite: true);
            ApplyFileAcl(target);
        }
        finally
        {
            try
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
            catch (IOException)
            {
                // Best-effort cleanup of a uniquely named temporary file.
            }
        }
    }

    private static void Validate(EmergencyUnlockRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (record.SchemaVersion != SchemaVersion)
            throw new InvalidDataException("Schema emergency unlock tidak didukung.");
        if (!string.Equals(
                record.MachineName,
                Environment.MachineName,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Emergency unlock dibuat untuk komputer lain.");
        if (record.ExpiresAtUtc <= record.IssuedAtUtc
            || record.ExpiresAtUtc - record.IssuedAtUtc
                > TimeSpan.FromMinutes(MaximumDurationMinutes))
            throw new InvalidDataException("Rentang waktu emergency unlock tidak valid.");
        if (string.IsNullOrWhiteSpace(record.IssuedBy)
            || record.IssuedBy.Length > 128
            || string.IsNullOrWhiteSpace(record.Reason)
            || record.Reason.Length > 256)
            throw new InvalidDataException("Identitas atau alasan emergency unlock tidak valid.");
    }

    private static string Normalize(string? value, int maximumLength, string fallback)
    {
        var result = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        return result.Length <= maximumLength ? result : result[..maximumLength];
    }

    private static void ApplyDirectoryAcl(string path)
    {
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(Rule(
            WellKnownSidType.LocalSystemSid,
            FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit));
        security.AddAccessRule(Rule(
            WellKnownSidType.BuiltinAdministratorsSid,
            FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit));
        security.AddAccessRule(Rule(
            WellKnownSidType.BuiltinUsersSid,
            FileSystemRights.ReadAndExecute,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit));
        new DirectoryInfo(path).SetAccessControl(security);
    }

    private static void ApplyFileAcl(string path)
    {
        var security = new FileSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(Rule(
            WellKnownSidType.LocalSystemSid,
            FileSystemRights.FullControl,
            InheritanceFlags.None));
        security.AddAccessRule(Rule(
            WellKnownSidType.BuiltinAdministratorsSid,
            FileSystemRights.FullControl,
            InheritanceFlags.None));
        security.AddAccessRule(Rule(
            WellKnownSidType.BuiltinUsersSid,
            FileSystemRights.ReadAndExecute,
            InheritanceFlags.None));
        new FileInfo(path).SetAccessControl(security);
    }

    private static FileSystemAccessRule Rule(
        WellKnownSidType sid,
        FileSystemRights rights,
        InheritanceFlags inheritance) => new(
        new SecurityIdentifier(sid, null),
        rights,
        inheritance,
        PropagationFlags.None,
        AccessControlType.Allow);

    private static void EnsureWindows()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException(
                "Emergency unlock LabKom hanya tersedia di Windows.");
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };
}

public sealed record EmergencyUnlockRecord(
    int SchemaVersion,
    string MachineName,
    DateTimeOffset IssuedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    string IssuedBy,
    string Reason);
