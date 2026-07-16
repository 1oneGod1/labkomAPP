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

    public static ActivityEntryViewModel FromCommandResult(CommandResult result)
    {
        var local = DateTimeOffset.FromUnixTimeMilliseconds(result.TimestampUnixMs).LocalDateTime;
        var color = result.State switch
        {
            CommandExecutionState.Accepted or CommandExecutionState.Applied => "#10B981",
            CommandExecutionState.Rejected => "#F59E0B",
            CommandExecutionState.Failed => "#EF4444",
            _ => "#64748B",
        };

        return new ActivityEntryViewModel
        {
            PcName = result.PcName,
            Title = result.Message ?? $"{result.Kind}: {result.State}",
            ProcessName = result.CommandId[..8],
            TimeLabel = local.ToString("HH:mm:ss"),
            KindLabel = result.State.ToString(),
            KindColor = color,
        };
    }

    public static ActivityEntryViewModel FromChat(ChatMessage message)
    {
        var local = DateTimeOffset.FromUnixTimeMilliseconds(message.TimestampUnixMs).LocalDateTime;
        return new ActivityEntryViewModel
        {
            PcName = message.FromPcName ?? "Siswa",
            Title = message.Body,
            ProcessName = null,
            TimeLabel = local.ToString("HH:mm:ss"),
            KindLabel = "Pesan",
            KindColor = "#A855F7",
        };
    }

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
