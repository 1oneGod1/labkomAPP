using LabKom.Shared.Security;

namespace LabKom.Provisioning;

internal static class SecurityAdministrationCommands
{
    public static int KeyStatus() =>
        AdministrativeSecurityContext.Execute(
            "key.status",
            target: null,
            TeacherPermission.ViewAudit.ToString(),
            () =>
            {
                var policy = KeyRotationPolicyStore.ReadOrDefault();
                Console.WriteLine($"Versi kunci aktif : {policy.CurrentVersion}");
                Console.WriteLine($"Versi grace       : {policy.AcceptedPreviousVersions}");
                Console.WriteLine($"Rotasi terakhir  : {policy.RotatedAtUtc:O}");
                return 0;
            });

    public static int RotateKey(IReadOnlyDictionary<string, string?> options)
    {
        if (!options.ContainsKey("--yes")
            && !Confirm("ROTATE", "Naikkan versi credential seluruh perangkat satu tingkat?"))
            return 2;
        return AdministrativeSecurityContext.Execute(
            "key.rotate",
            target: null,
            TeacherPermission.ManageDevices.ToString(),
            () =>
            {
                var policy = KeyRotationPolicyStore.Advance();
                Console.WriteLine($"Versi kunci aktif sekarang: {policy.CurrentVersion}");
                Console.WriteLine(
                    "Agent dengan versi grace akan menerima challenge rotasi saat heartbeat.");
                return 0;
            });
    }

    public static int ListRbac() =>
        AdministrativeSecurityContext.Execute(
            "rbac.list",
            target: null,
            TeacherPermission.ViewAudit.ToString(),
            () =>
            {
                var document = RbacAssignmentStore.ReadOrDefault();
                if (document.Assignments.Count == 0)
                {
                    Console.WriteLine(
                        "Belum ada assignment eksplisit. Local Administrators tetap berperan Administrator.");
                    return 0;
                }

                foreach (var assignment in document.Assignments)
                    Console.WriteLine($"{assignment.Sid} = {assignment.Role}");
                return 0;
            });

    public static int GrantRbac(IReadOnlyDictionary<string, string?> options)
    {
        var sid = Require(options, "--sid");
        var rawRole = Require(options, "--role");
        if (!Enum.TryParse<LabKomRole>(rawRole, ignoreCase: true, out var role)
            || !Enum.IsDefined(role))
            throw new ArgumentException("--role harus Observer, Auditor, Instructor, atau Administrator.");
        return AdministrativeSecurityContext.Execute(
            "rbac.grant",
            sid,
            TeacherPermission.ManageDevices.ToString(),
            () =>
            {
                _ = RbacAssignmentStore.Grant(sid, role);
                Console.WriteLine($"RBAC diperbarui: {sid} = {role}");
                return 0;
            });
    }

    public static int RevokeRbac(IReadOnlyDictionary<string, string?> options)
    {
        var sid = Require(options, "--sid");
        return AdministrativeSecurityContext.Execute(
            "rbac.revoke",
            sid,
            TeacherPermission.ManageDevices.ToString(),
            () =>
            {
                _ = RbacAssignmentStore.Revoke(sid);
                Console.WriteLine($"Assignment RBAC dihapus: {sid}");
                return 0;
            });
    }

    public static int DeviceStatus() =>
        AdministrativeSecurityContext.Execute(
            "device.status",
            Environment.MachineName,
            TeacherPermission.ViewAudit.ToString(),
            () =>
            {
                if (!DeviceCredentialStore.TryRead(out var credential)
                    || credential is null)
                {
                    Console.WriteLine("Credential perangkat belum dibuat.");
                    return 0;
                }

                Console.WriteLine($"Device ID      : {credential.DeviceId}");
                Console.WriteLine($"PC             : {credential.PcName}");
                Console.WriteLine($"Versi kunci    : {credential.KeyVersion}");
                Console.WriteLine($"Credential ID  : {credential.CredentialId}");
                Console.WriteLine($"Dibuat UTC     : {credential.CreatedAtUtc:O}");
                return 0;
            });

    public static int EnrollDevice(IReadOnlyDictionary<string, string?> options)
    {
        var rawVersion = Require(options, "--key-version");
        if (!int.TryParse(
                rawVersion,
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out var keyVersion)
            || keyVersion is < 1 or > 100_000)
            throw new ArgumentException("--key-version harus 1-100000.");

        return AdministrativeSecurityContext.Execute(
            "device.enroll",
            Environment.MachineName,
            TeacherPermission.ManageDevices.ToString(),
            () =>
            {
                var provisioning = ProvisionedSecretStore.Read();
                var credential = DeviceCredentialStore.EnsureFromProvisioning(
                    provisioning,
                    Environment.MachineName,
                    keyVersion);
                Console.WriteLine($"Device ID     : {credential.DeviceId}");
                Console.WriteLine($"Versi kunci   : {credential.KeyVersion}");
                Console.WriteLine($"Credential ID : {credential.CredentialId}");
                return 0;
            });
    }

    public static int VerifyAudit()
    {
        _ = AdministrativeSecurityContext.RequireLocalAdministrator();
        var journal = AdministrativeSecurityContext.OpenJournal();
        Console.WriteLine($"Audit path       : {SecurityAuditJournal.DefaultMachinePath}");
        Console.WriteLine($"Jumlah record    : {journal.LastSequence}");
        Console.WriteLine($"Integritas audit : {(journal.IntegrityValid ? "VALID" : "RUSAK")}");
        return journal.IntegrityValid ? 0 : 3;
    }

    public static int TailAudit(IReadOnlyDictionary<string, string?> options)
    {
        var maximum = 50;
        if (options.TryGetValue("--count", out var rawCount)
            && (!int.TryParse(
                    rawCount,
                    System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out maximum)
                || maximum is < 1 or > 1000))
            throw new ArgumentException("--count harus 1-1000.");

        return AdministrativeSecurityContext.Execute(
            "audit.read",
            target: null,
            TeacherPermission.ViewAudit.ToString(),
            () =>
            {
                var journal = AdministrativeSecurityContext.OpenJournal();
                foreach (var record in journal.ReadRecent(maximum))
                {
                    Console.WriteLine(
                        $"{record.Sequence,6} {record.TimestampUtc:O} "
                        + $"{record.ActorName} [{record.Role}] {record.Action} "
                        + $"target={record.Target ?? "-"} outcome={record.Outcome} "
                        + $"detail={record.Detail ?? "-"}");
                }
                return 0;
            });
    }

    private static bool Confirm(string expected, string message)
    {
        Console.WriteLine(message);
        Console.Write($"Ketik {expected} untuk melanjutkan: ");
        return string.Equals(Console.ReadLine()?.Trim(), expected, StringComparison.Ordinal);
    }

    private static string Require(
        IReadOnlyDictionary<string, string?> options,
        string key) =>
        options.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new ArgumentException($"{key} wajib diberikan.");
}
