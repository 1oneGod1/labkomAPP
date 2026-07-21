using System.Globalization;
using System.IO;
using System.Text;
using LabKom.Shared.Contracts;
using LabKom.Shared.Security;

namespace LabKom.Teacher.Services;

/// <summary>
/// Console teknisi terikat RBAC. Tidak menyediakan shell atau eksekusi arbitrary;
/// aksi hanya refresh stream dan emergency unlock yang sudah diaudit.
/// </summary>
public sealed class TechnicianConsoleService
{
    private readonly PresenceRegistry _presence;
    private readonly TelemetryRegistry _telemetry;
    private readonly RemoteCommandService _remote;
    private readonly TeacherAuthorizationService _authorization;
    private readonly SecurityAuditJournal _audit;

    public TechnicianConsoleService(
        PresenceRegistry presence,
        TelemetryRegistry telemetry,
        RemoteCommandService remote,
        TeacherAuthorizationService authorization,
        SecurityAuditJournal audit)
    {
        _presence = presence;
        _telemetry = telemetry;
        _remote = remote;
        _authorization = authorization;
        _audit = audit;
    }

    public TechnicianSummary Snapshot()
    {
        _authorization.Demand(
            TeacherPermission.TechnicianConsole,
            "technician.view",
            null);
        ProvisionedSecretRecord? provisioning = null;
        try
        {
            ProvisionedSecretStore.TryRead(null, out provisioning);
        }
        catch
        {
            // Ditampilkan sebagai belum tersedia.
        }
        var rotation = KeyRotationPolicyStore.ReadOrDefault();
        var rows = _presence.Snapshot()
            .Select(entry =>
            {
                var telemetry = _telemetry.Get(entry.PcName);
                var memory = telemetry is null
                    ? 0
                    : Percent(
                        telemetry.Telemetry.UsedMemoryBytes,
                        telemetry.Telemetry.TotalMemoryBytes);
                return new TechnicianDeviceRow(
                    entry.PcName,
                    entry.Status.ToString(),
                    !string.IsNullOrWhiteSpace(entry.ConnectionId),
                    !string.IsNullOrWhiteSpace(entry.DesktopConnectionId),
                    entry.LastSeenUtc,
                    telemetry?.Health.ToString() ?? "No telemetry",
                    telemetry?.Telemetry.CpuPercent ?? 0,
                    memory,
                    telemetry?.LatencyMs ?? 0,
                    entry.LastFrame?.CaptureBackend.ToString() ?? "-",
                    entry.MonitorInventory?.Monitors.Count ?? 0);
            })
            .OrderBy(row => row.PcName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return new TechnicianSummary(
            typeof(TechnicianConsoleService).Assembly
                .GetName().Version?.ToString(3) ?? "0.0.0",
            provisioning?.ClassroomName ?? "Belum diprovisikan",
            provisioning?.ClassroomId ?? "-",
            rotation.CurrentVersion,
            rotation.AcceptedPreviousVersions,
            _audit.IntegrityValid,
            rows);
    }

    public Task EmergencyUnlockAsync(string pcName) =>
        _authorization.ExecuteAsync(
            TeacherPermission.TechnicianConsole,
            "technician.emergency-unlock",
            pcName,
            () => _remote.UnlockAsync(pcName));

    public Task RefreshStreamAsync(string pcName) =>
        _authorization.ExecuteAsync(
            TeacherPermission.TechnicianConsole,
            "technician.refresh-stream",
            pcName,
            () => _remote.SetCaptureProfileAsync(
                pcName,
                CaptureProfile.Focus));

    public void ExportCsv(
        string path,
        IReadOnlyCollection<TechnicianDeviceRow> rows)
    {
        _authorization.Execute(
            TeacherPermission.TechnicianConsole,
            "technician.export",
            path,
            () =>
            {
                var output = new StringBuilder();
                output.AppendLine(
                    "PcName,Status,Agent,Desktop,LastSeenUtc,Health,CpuPercent,MemoryPercent,LatencyMs,CaptureBackend,Monitors");
                foreach (var row in rows)
                {
                    output.AppendLine(string.Join(
                        ",",
                        Csv(row.PcName),
                        Csv(row.Status),
                        row.AgentConnected,
                        row.DesktopConnected,
                        row.LastSeenUtc.ToString("O"),
                        Csv(row.Health),
                        row.CpuPercent.ToString("F1", CultureInfo.InvariantCulture),
                        row.MemoryPercent.ToString("F1", CultureInfo.InvariantCulture),
                        row.LatencyMs.ToString("F1", CultureInfo.InvariantCulture),
                        Csv(row.CaptureBackend),
                        row.MonitorCount));
                }
                File.WriteAllText(path, output.ToString(), new UTF8Encoding(true));
                return true;
            });
    }

    private static double Percent(long value, long total) =>
        total <= 0 ? 0 : 100d * value / total;

    private static string Csv(string value) =>
        "\"" + value.Replace("\"", "\"\"") + "\"";
}

public sealed record TechnicianSummary(
    string Version,
    string ClassroomName,
    string ClassroomId,
    int KeyVersion,
    int AcceptedPreviousKeyVersions,
    bool AuditIntegrityValid,
    IReadOnlyList<TechnicianDeviceRow> Devices);

public sealed record TechnicianDeviceRow(
    string PcName,
    string Status,
    bool AgentConnected,
    bool DesktopConnected,
    DateTime LastSeenUtc,
    string Health,
    double CpuPercent,
    double MemoryPercent,
    double LatencyMs,
    string CaptureBackend,
    int MonitorCount);
