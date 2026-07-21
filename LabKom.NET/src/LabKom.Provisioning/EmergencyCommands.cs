using System.Globalization;
using LabKom.Shared.Security;

namespace LabKom.Provisioning;

internal static class EmergencyCommands
{
    public static int Unlock(IReadOnlyDictionary<string, string?> options)
    {
        var rawMinutes = Value(options, "--minutes")
                         ?? EmergencyUnlockStore.DefaultDurationMinutes.ToString(
                             CultureInfo.InvariantCulture);
        if (!int.TryParse(
                rawMinutes,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var minutes)
            || minutes is < 1 or > EmergencyUnlockStore.MaximumDurationMinutes)
        {
            throw new ArgumentException(
                $"--minutes harus 1-{EmergencyUnlockStore.MaximumDurationMinutes}.");
        }

        if (!options.ContainsKey("--yes")
            && !Confirm("UNLOCK", $"Lepas kontrol LabKom selama {minutes} menit?"))
        {
            Console.WriteLine("Emergency unlock dibatalkan.");
            return 2;
        }

        var reason = Value(options, "--reason");
        return AdministrativeSecurityContext.Execute(
            "emergency.unlock",
            Environment.MachineName,
            $"{TeacherPermission.EmergencyUnlock};minutes={minutes};reason={reason ?? "-"}",
            () =>
            {
                var record = EmergencyUnlockStore.Activate(
                    TimeSpan.FromMinutes(minutes),
                    reason);
                Console.WriteLine("EMERGENCY UNLOCK AKTIF");
                Console.WriteLine($"Komputer : {record.MachineName}");
                Console.WriteLine($"Admin    : {record.IssuedBy}");
                Console.WriteLine(
                    $"Berakhir : {record.ExpiresAtUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss zzz}");
                Console.WriteLine(
                    "Student Desktop akan melepas overlay dan hook dalam beberapa detik.");
                return 0;
            });
    }

    public static int Status() =>
        AdministrativeSecurityContext.Execute(
            "emergency.status",
            Environment.MachineName,
            TeacherPermission.ViewAudit.ToString(),
            () =>
            {
                if (!EmergencyUnlockStore.TryRead(out var record))
                {
                    Console.WriteLine(
                        "Emergency unlock tidak aktif dan belum pernah dibuat.");
                    return 0;
                }

                var active = EmergencyUnlockStore.IsActive(
                    record,
                    DateTimeOffset.UtcNow);
                Console.WriteLine(active
                    ? "Emergency unlock AKTIF."
                    : "Emergency unlock sudah kedaluwarsa.");
                Console.WriteLine($"Admin    : {record.IssuedBy}");
                Console.WriteLine($"Alasan   : {record.Reason}");
                Console.WriteLine(
                    $"Berakhir : {record.ExpiresAtUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss zzz}");
                return 0;
            });

    public static int Clear(IReadOnlyDictionary<string, string?> options)
    {
        if (!options.ContainsKey("--yes")
            && !Confirm("CLEAR", "Hapus emergency override dan sinkronkan kembali state Teacher?"))
        {
            Console.WriteLine("Penghapusan emergency unlock dibatalkan.");
            return 2;
        }

        return AdministrativeSecurityContext.Execute(
            "emergency.clear",
            Environment.MachineName,
            TeacherPermission.EmergencyUnlock.ToString(),
            () =>
            {
                EmergencyUnlockStore.Clear();
                Console.WriteLine(
                    "Emergency unlock dihapus. Student Desktop akan sinkron ulang ke Teacher.");
                return 0;
            });
    }

    private static bool Confirm(string expected, string message)
    {
        Console.WriteLine(message);
        Console.Write($"Ketik {expected} untuk melanjutkan: ");
        return string.Equals(
            Console.ReadLine()?.Trim(),
            expected,
            StringComparison.Ordinal);
    }

    private static string? Value(
        IReadOnlyDictionary<string, string?> options,
        string key) =>
        options.TryGetValue(key, out var value)
        && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;
}
