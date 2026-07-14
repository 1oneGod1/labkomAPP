using LabKom.Shared.Contracts;

namespace LabKom.Teacher.ViewModels;

public sealed class ActivityEntryViewModel
{
    public string PcName { get; init; } = "";
    public string Title { get; init; } = "";
    public string? ProcessName { get; init; }
    public string TimeLabel { get; init; } = "";
    public string KindLabel { get; init; } = "";
    public string KindColor { get; init; } = "#94A3B8";

    public static ActivityEntryViewModel From(ActivityRecord r)
    {
        var (label, color) = r.Kind switch
        {
            ActivityRecordKind.WindowChange => ("Aktif", "#3B82F6"),
            ActivityRecordKind.ProcessStart => ("Buka", "#10B981"),
            ActivityRecordKind.ProcessStop => ("Tutup", "#F59E0B"),
            ActivityRecordKind.Idle => ("Idle", "#94A3B8"),
            _ => ("Sistem", "#64748B"),
        };

        var local = DateTimeOffset.FromUnixTimeMilliseconds(r.TimestampUnixMs).LocalDateTime;
        return new ActivityEntryViewModel
        {
            PcName = r.PcName,
            Title = r.Title,
            ProcessName = r.ProcessName,
            TimeLabel = local.ToString("HH:mm:ss"),
            KindLabel = label,
            KindColor = color,
        };
    }
}
