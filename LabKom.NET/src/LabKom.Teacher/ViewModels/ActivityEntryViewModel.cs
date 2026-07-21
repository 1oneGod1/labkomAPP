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

    public static ActivityEntryViewModel FromFileProgress(
        FileDistributionProgress progress)
    {
        var local = DateTimeOffset.FromUnixTimeMilliseconds(
            progress.TimestampUnixMs).LocalDateTime;
        var (label, color) = progress.State switch
        {
            FileDistributionState.Downloading => ("Transfer", "#3B82F6"),
            FileDistributionState.Completed => ("Selesai", "#10B981"),
            FileDistributionState.Failed => ("Gagal", "#EF4444"),
            _ => ("Dikirim", "#64748B"),
        };
        var title = progress.State switch
        {
            FileDistributionState.Downloading =>
                $"Mengunduh file ({FormatBytes(progress.BytesReceived)})",
            FileDistributionState.Completed =>
                $"File diterima ({FormatBytes(progress.BytesReceived)})",
            FileDistributionState.Failed =>
                $"Transfer file gagal: {progress.ErrorMessage ?? "kesalahan tidak diketahui"}",
            _ => "File siap diunduh",
        };

        return new ActivityEntryViewModel
        {
            PcName = progress.PcName,
            Title = title,
            ProcessName = $"ID {progress.NoticeId[..Math.Min(8, progress.NoticeId.Length)]}",
            TimeLabel = local.ToString("HH:mm:ss"),
            KindLabel = label,
            KindColor = color,
        };
    }
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
            ActivityRecordKind.UsageSample => ("Monitor", "#06B6D4"),
            ActivityRecordKind.FileCollected => ("Collect", "#8B5CF6"),
            ActivityRecordKind.Registration => ("Register", "#22C55E"),
            ActivityRecordKind.Assessment => ("Nilai", "#F97316"),
            _ => ("Sistem", "#64748B"),
        };

        var local = DateTimeOffset.FromUnixTimeMilliseconds(r.TimestampUnixMs).LocalDateTime;
        return new ActivityEntryViewModel
        {
            PcName = r.PcName,
            Title = r.Metrics is null
                ? r.Title
                : $"{r.Title} ? keyboard " +
                  $"{r.Metrics.KeyboardEventCount} ? idle " +
                  $"{TimeSpan.FromMilliseconds(r.Metrics.IdleMilliseconds):mm\\:ss}",
            ProcessName = r.Metrics is null
                ? r.ProcessName
                : $"{r.ProcessName} ? {r.Metrics.Category}",
            TimeLabel = local.ToString("HH:mm:ss"),
            KindLabel = label,
            KindColor = color,
        };
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1024L * 1024 * 1024)
        {
            return $"{bytes / (1024d * 1024 * 1024):0.##} GB";
        }

        if (bytes >= 1024L * 1024)
        {
            return $"{bytes / (1024d * 1024):0.##} MB";
        }

        if (bytes >= 1024)
        {
            return $"{bytes / 1024d:0.##} KB";
        }

        return $"{bytes} B";
    }
}
