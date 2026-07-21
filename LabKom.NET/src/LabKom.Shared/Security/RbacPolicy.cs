using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;

namespace LabKom.Shared.Security;

public enum LabKomRole
{
    Observer = 1,
    Auditor = 2,
    Instructor = 3,
    Administrator = 4,
}

public enum TeacherPermission
{
    ViewClassroom = 1,
    ViewAudit = 2,
    ManageAttention = 3,
    SendMessage = 4,
    DistributeFiles = 5,
    BroadcastScreen = 6,
    ManagePolicies = 7,
    ManagePower = 8,
    ManageDevices = 9,
    EmergencyUnlock = 10,
    RemoteControl = 11,
    CollectFiles = 12,
    ManageLessons = 13,
    TechnicianConsole = 14,
}

public static class RbacPolicy
{
    public static bool IsAllowed(LabKomRole role, TeacherPermission permission) =>
        role switch
        {
            LabKomRole.Administrator => true,
            LabKomRole.Instructor => permission is
                TeacherPermission.ViewClassroom
                or TeacherPermission.ManageAttention
                or TeacherPermission.SendMessage
                or TeacherPermission.DistributeFiles
                or TeacherPermission.BroadcastScreen
                or TeacherPermission.ManagePolicies
                or TeacherPermission.ManagePower
                or TeacherPermission.RemoteControl
                or TeacherPermission.CollectFiles
                or TeacherPermission.ManageLessons,
            LabKomRole.Auditor => permission is
                TeacherPermission.ViewClassroom
                or TeacherPermission.ViewAudit,
            LabKomRole.Observer => permission == TeacherPermission.ViewClassroom,
            _ => false,
        };
}

[SupportedOSPlatform("windows")]
public static class RbacAssignmentStore
{
    public const int SchemaVersion = 1;

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "LabKom",
        "Security",
        "rbac-policy.json");

    public static RbacAssignmentDocument ReadOrDefault(string? path = null)
    {
        var target = Path.GetFullPath(path ?? DefaultPath);
        if (!File.Exists(target))
            return new RbacAssignmentDocument(
                SchemaVersion,
                DateTimeOffset.MinValue,
                Array.Empty<RbacAssignment>());
        var document = JsonSerializer.Deserialize<RbacAssignmentDocument>(
            File.ReadAllText(target),
            JsonOptions)
            ?? throw new InvalidDataException("Policy RBAC kosong.");
        Validate(document);
        return document;
    }

    public static RbacAssignmentDocument Grant(
        string sid,
        LabKomRole role,
        string? path = null)
    {
        EnsureWindows();
        _ = new SecurityIdentifier(sid);
        if (!Enum.IsDefined(role)) throw new ArgumentOutOfRangeException(nameof(role));
        var current = ReadOrDefault(path);
        var assignments = current.Assignments
            .Where(item => !string.Equals(item.Sid, sid, StringComparison.OrdinalIgnoreCase))
            .Append(new RbacAssignment(sid, role))
            .OrderBy(item => item.Sid, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var updated = new RbacAssignmentDocument(
            SchemaVersion,
            DateTimeOffset.UtcNow,
            assignments);
        Write(updated, path);
        return updated;
    }

    public static RbacAssignmentDocument Revoke(string sid, string? path = null)
    {
        EnsureWindows();
        _ = new SecurityIdentifier(sid);
        var current = ReadOrDefault(path);
        var updated = new RbacAssignmentDocument(
            SchemaVersion,
            DateTimeOffset.UtcNow,
            current.Assignments
                .Where(item => !string.Equals(item.Sid, sid, StringComparison.OrdinalIgnoreCase))
                .ToArray());
        Write(updated, path);
        return updated;
    }

    public static LabKomRole? FindRole(string sid, string? path = null) =>
        ReadOrDefault(path).Assignments
            .FirstOrDefault(item => string.Equals(
                item.Sid,
                sid,
                StringComparison.OrdinalIgnoreCase))
            ?.Role;

    private static void Write(RbacAssignmentDocument document, string? path)
    {
        Validate(document);
        var target = Path.GetFullPath(path ?? DefaultPath);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(document, JsonOptions));
            ApplyAcl(temporary);
            File.Move(temporary, target, overwrite: true);
            ApplyAcl(target);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static void Validate(RbacAssignmentDocument document)
    {
        if (document.SchemaVersion != SchemaVersion || document.Assignments.Count > 256)
            throw new InvalidDataException("Policy RBAC tidak valid.");
        var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var assignment in document.Assignments)
        {
            try
            {
                _ = new SecurityIdentifier(assignment.Sid);
            }
            catch (ArgumentException exception)
            {
                throw new InvalidDataException("SID RBAC tidak valid.", exception);
            }
            if (!Enum.IsDefined(assignment.Role) || !unique.Add(assignment.Sid))
                throw new InvalidDataException("Assignment RBAC duplikat atau tidak valid.");
        }
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
            throw new PlatformNotSupportedException("RBAC LabKom hanya tersedia di Windows.");
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };
}

public sealed record RbacAssignment(string Sid, LabKomRole Role);

public sealed record RbacAssignmentDocument(
    int SchemaVersion,
    DateTimeOffset UpdatedAtUtc,
    IReadOnlyList<RbacAssignment> Assignments);
