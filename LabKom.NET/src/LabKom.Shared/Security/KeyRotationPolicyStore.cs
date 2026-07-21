using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;

namespace LabKom.Shared.Security;

[SupportedOSPlatform("windows")]
public static class KeyRotationPolicyStore
{
    public const int SchemaVersion = 1;
    public const int DefaultAcceptedPreviousVersions = 1;

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "LabKom",
        "Security",
        "key-rotation.json");

    public static KeyRotationPolicy ReadOrDefault(string? path = null)
    {
        var target = Path.GetFullPath(path ?? DefaultPath);
        if (!File.Exists(target))
            return new KeyRotationPolicy(
                SchemaVersion,
                DeviceCredentialStore.DefaultKeyVersion,
                DefaultAcceptedPreviousVersions,
                DateTimeOffset.MinValue);
        var policy = JsonSerializer.Deserialize<KeyRotationPolicy>(
            File.ReadAllText(target),
            JsonOptions)
            ?? throw new InvalidDataException("Policy rotasi kosong.");
        Validate(policy);
        return policy;
    }

    public static KeyRotationPolicy Advance(string? path = null)
    {
        EnsureWindows();
        var current = ReadOrDefault(path);
        var next = current with
        {
            CurrentVersion = checked(current.CurrentVersion + 1),
            RotatedAtUtc = DateTimeOffset.UtcNow,
        };
        Write(next, path);
        return next;
    }

    public static void Write(KeyRotationPolicy policy, string? path = null)
    {
        EnsureWindows();
        Validate(policy);
        var target = Path.GetFullPath(path ?? DefaultPath);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(policy, JsonOptions));
            ApplyAcl(temporary);
            File.Move(temporary, target, overwrite: true);
            ApplyAcl(target);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static void Validate(KeyRotationPolicy policy)
    {
        if (policy.SchemaVersion != SchemaVersion
            || policy.CurrentVersion is < 1 or > 100_000
            || policy.AcceptedPreviousVersions is < 0 or > 10)
            throw new InvalidDataException("Policy rotasi kunci tidak valid.");
    }

    private static void ApplyAcl(string path)
    {
        var security = new FileSecurity();
        security.SetAccessRuleProtection(true, false);
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
            throw new PlatformNotSupportedException("Rotasi kunci hanya tersedia di Windows.");
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };
}

public sealed record KeyRotationPolicy(
    int SchemaVersion,
    int CurrentVersion,
    int AcceptedPreviousVersions,
    DateTimeOffset RotatedAtUtc);
