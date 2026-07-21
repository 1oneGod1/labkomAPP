using LabKom.Shared.Security;

namespace LabKom.Provisioning;

internal static class Program
{
    private const string Usage =
        """
        LabKom.Provisioning create --output <file> [--classroom <name>] [--install-local]
        LabKom.Provisioning install --bundle <file>
        LabKom.Provisioning export --output <file>
        LabKom.Provisioning verify
        LabKom.Provisioning emergency-unlock [--minutes 15] [--reason <text>] [--yes]
        LabKom.Provisioning emergency-status
        LabKom.Provisioning emergency-clear [--yes]
        LabKom.Provisioning key-status
        LabKom.Provisioning device-status
        LabKom.Provisioning device-enroll --key-version <version>
        LabKom.Provisioning key-rotate [--yes]
        LabKom.Provisioning rbac-list
        LabKom.Provisioning audit-verify
        LabKom.Provisioning audit-tail [--count 50]
        LabKom.Provisioning rbac-grant --sid <windows-sid> --role <role>
        LabKom.Provisioning rbac-revoke --sid <windows-sid>

        Bundle provisioning berisi credential kelas dalam bentuk plaintext.
        Simpan offline, batasi akses, dan jangan commit ke Git.
        """;

    public static int Main(string[] args)
    {
        try
        {
            if (args.Length == 0) throw new ArgumentException("Perintah belum diberikan.");
            var command = args[0].ToLowerInvariant();
            var options = ParseOptions(args.Skip(1).ToArray());
            return command switch
            {
                "create" => Create(options),
                "install" => Install(options),
                "export" => Export(options),
                "verify" => Verify(),
                "emergency-unlock" => EmergencyCommands.Unlock(options),
                "emergency-status" => EmergencyCommands.Status(),
                "emergency-clear" => EmergencyCommands.Clear(options),
                "device-status" => SecurityAdministrationCommands.DeviceStatus(),
                "device-enroll" => SecurityAdministrationCommands.EnrollDevice(options),
                "key-status" => SecurityAdministrationCommands.KeyStatus(),
                "key-rotate" => SecurityAdministrationCommands.RotateKey(options),
                "rbac-list" => SecurityAdministrationCommands.ListRbac(),
                "rbac-grant" => SecurityAdministrationCommands.GrantRbac(options),
                "audit-verify" => SecurityAdministrationCommands.VerifyAudit(),
                "audit-tail" => SecurityAdministrationCommands.TailAudit(options),
                "rbac-revoke" => SecurityAdministrationCommands.RevokeRbac(options),
                _ => throw new ArgumentException($"Perintah '{args[0]}' tidak dikenal."),
            };
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Gagal: {exception.Message}");
            Console.Error.WriteLine(Usage);
            return 1;
        }
    }

    private static int Create(IReadOnlyDictionary<string, string?> options)
    {
        var output = Require(options, "--output");
        var record = ProvisionedSecretStore.Create(Value(options, "--classroom"));
        ProvisionedSecretStore.ExportProvisioningBundle(record, output);
        if (options.ContainsKey("--install-local"))
            ProvisionedSecretStore.Write(record);

        Console.WriteLine($"Bundle provisioning dibuat: {Path.GetFullPath(output)}");
        Console.WriteLine($"Classroom ID: {record.ClassroomId}");
        Console.WriteLine("PENTING: bundle berisi secret. Simpan offline dan jangan kirim melalui chat/email.");
        return 0;
    }

    private static int Install(IReadOnlyDictionary<string, string?> options)
    {
        var bundle = Require(options, "--bundle");
        var record = ProvisionedSecretStore.ImportProvisioningBundle(bundle);
        ProvisionedSecretStore.Write(record);
        Console.WriteLine($"Provisioning '{record.ClassroomName}' terpasang untuk mesin ini.");
        Console.WriteLine($"Classroom ID: {record.ClassroomId}");
        return 0;
    }

    private static int Export(IReadOnlyDictionary<string, string?> options)
    {
        var output = Require(options, "--output");
        var record = ProvisionedSecretStore.Read();
        ProvisionedSecretStore.ExportProvisioningBundle(record, output);
        Console.WriteLine($"Bundle provisioning diekspor: {Path.GetFullPath(output)}");
        Console.WriteLine("PENTING: amankan file tersebut dan hapus dari USB setelah deployment selesai.");
        return 0;
    }

    private static int Verify()
    {
        var record = ProvisionedSecretStore.Read();
        Console.WriteLine($"Provisioning valid: {record.ClassroomName} ({record.ClassroomId})");
        Console.WriteLine($"Dibuat UTC: {record.CreatedAtUtc:O}");
        return 0;
    }

    private static Dictionary<string, string?> ParseOptions(string[] args)
    {
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < args.Length; index++)
        {
            var key = args[index];
            if (!key.StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException($"Argumen '{key}' tidak valid.");
            if (string.Equals(key, "--install-local", StringComparison.OrdinalIgnoreCase)
                || string.Equals(key, "--yes", StringComparison.OrdinalIgnoreCase))
            {
                result[key] = null;
                continue;
            }
            if (++index >= args.Length) throw new ArgumentException($"Nilai '{key}' belum diberikan.");
            result[key] = args[index];
        }
        return result;
    }

    private static string Require(IReadOnlyDictionary<string, string?> options, string key) =>
        Value(options, key) ?? throw new ArgumentException($"{key} wajib diberikan.");

    private static string? Value(IReadOnlyDictionary<string, string?> options, string key) =>
        options.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;
}
