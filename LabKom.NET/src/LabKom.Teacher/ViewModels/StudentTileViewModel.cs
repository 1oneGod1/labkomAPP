using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using LabKom.Shared.Contracts;
using LabKom.Teacher.Services;

namespace LabKom.Teacher.ViewModels;

public partial class StudentTileViewModel : ObservableObject
{
    private string? _lastFrameStreamId;
    private long _lastFrameSequence;

    [ObservableProperty] private string _pcName = string.Empty;
    [ObservableProperty] private string _ipAddress = string.Empty;
    [ObservableProperty] private string _macAddress = string.Empty;
    [ObservableProperty] private string? _studentName;
    [ObservableProperty] private StudentStatus _status;
    [ObservableProperty] private DateTime _lastSeen;
    [ObservableProperty] private BitmapImage? _thumbnail;
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private string _currentMonitorId = string.Empty;
    [ObservableProperty] private string _frameResolution = string.Empty;
    [ObservableProperty] private string _streamQualityLabel = string.Empty;
    [ObservableProperty] private string _telemetryLabel = "Telemetry belum tersedia";
    [ObservableProperty] private string _telemetryColor = "#64748B";
    [ObservableProperty] private double _cpuPercent;
    [ObservableProperty] private double _memoryPercent;
    [ObservableProperty] private double _diskFreePercent;
    [ObservableProperty] private double _telemetryLatencyMs;
    [ObservableProperty] private long _networkReceiveBytesPerSecond;
    [ObservableProperty] private long _networkSendBytesPerSecond;

    public ObservableCollection<MonitorOptionViewModel> Monitors { get; } = new();

    public string StatusLabel => Status switch
    {
        StudentStatus.Offline => "Offline",
        StudentStatus.Online => "Online",
        StudentStatus.LoggedIn => "Login",
        StudentStatus.Locked => "Terkunci",
        _ => "?",
    };

    public string StatusColor => Status switch
    {
        StudentStatus.Offline => "#94A3B8",
        StudentStatus.Online => "#10B981",
        StudentStatus.LoggedIn => "#3B82F6",
        StudentStatus.Locked => "#F59E0B",
        _ => "#64748B",
    };

    partial void OnStatusChanged(StudentStatus value)
    {
        OnPropertyChanged(nameof(StatusLabel));
        OnPropertyChanged(nameof(StatusColor));
    }

    public void Apply(PresenceUpdate update)
    {
        Status = update.Status;
        LastSeen = DateTime.UtcNow;
        if (update.Snapshot is { } snapshot)
        {
            IpAddress = snapshot.IpAddress;
            MacAddress = snapshot.MacAddress;
            StudentName = snapshot.StudentName;
        }

        if (Status == StudentStatus.Offline)
        {
            Thumbnail = null;
            Monitors.Clear();
            CurrentMonitorId = string.Empty;
            FrameResolution = string.Empty;
            StreamQualityLabel = string.Empty;
            _lastFrameStreamId = null;
            _lastFrameSequence = 0;
            TelemetryLabel = "Telemetry offline";
            TelemetryColor = "#64748B";
        }
    }

    public void ApplyInventory(MonitorInventory inventory)
    {
        var options = inventory.Monitors
            .OrderByDescending(monitor => monitor.IsPrimary)
            .ThenBy(monitor => monitor.Left)
            .ThenBy(monitor => monitor.Top)
            .Select((monitor, index) => new MonitorOptionViewModel(
                monitor.Id,
                $"Monitor {index + 1} - {monitor.Width}x{monitor.Height}" +
                (monitor.IsPrimary ? " (Utama)" : string.Empty),
                monitor.IsPrimary))
            .ToArray();

        if (Monitors.SequenceEqual(options)) return;

        Monitors.Clear();
        foreach (var option in options)
        {
            Monitors.Add(option);
        }
    }

    public void ApplyTelemetry(DeviceTelemetrySnapshot snapshot)
    {
        var telemetry = snapshot.Telemetry;
        CpuPercent = telemetry.CpuPercent;
        MemoryPercent = Percent(
            telemetry.UsedMemoryBytes,
            telemetry.TotalMemoryBytes);
        DiskFreePercent = Percent(
            telemetry.DiskFreeBytes,
            telemetry.DiskTotalBytes);
        TelemetryLatencyMs = snapshot.LatencyMs;
        NetworkReceiveBytesPerSecond = telemetry.NetworkReceiveBytesPerSecond;
        NetworkSendBytesPerSecond = telemetry.NetworkSendBytesPerSecond;
        TelemetryColor = snapshot.Health switch
        {
            TelemetryHealth.Healthy => "#10B981",
            TelemetryHealth.Warning => "#F59E0B",
            TelemetryHealth.Critical => "#EF4444",
            _ => "#64748B",
        };
        TelemetryLabel =
            $"CPU {CpuPercent:F0}% ? RAM {MemoryPercent:F0}% ? Disk {DiskFreePercent:F0}% bebas"
            + Environment.NewLine
            + $"? {FormatRate(NetworkReceiveBytesPerSecond)}  ? {FormatRate(NetworkSendBytesPerSecond)}"
            + $" ? {TelemetryLatencyMs:F0} ms";
    }

    private static double Percent(long value, long total) =>
        total <= 0 ? 0 : 100d * value / total;

    private static string FormatRate(long bytesPerSecond) =>
        bytesPerSecond >= 1024 * 1024
            ? $"{bytesPerSecond / (1024d * 1024):F1} MB/s"
            : $"{bytesPerSecond / 1024d:F0} KB/s";

    public bool ApplyFrame(ScreenFrame frame)
    {
        if (frame.JpegData.Length == 0) return false;
        if (string.Equals(_lastFrameStreamId, frame.StreamId, StringComparison.Ordinal)
            && frame.SequenceNumber <= _lastFrameSequence)
        {
            return false;
        }

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = new MemoryStream(frame.JpegData);
            bitmap.EndInit();
            bitmap.Freeze();

            Thumbnail = bitmap;
            CurrentMonitorId = frame.MonitorId;
            FrameResolution = $"{frame.Width}x{frame.Height}";
            StreamQualityLabel =
                $"{frame.CaptureBackend} | {frame.TargetFramesPerSecond} fps Q{frame.JpegQuality}"
                + $" | capture {frame.CaptureDurationMilliseconds} ms"
                + $" / send {frame.PreviousSendDurationMilliseconds} ms";

            _lastFrameStreamId = frame.StreamId;
            _lastFrameSequence = frame.SequenceNumber;
            return true;
        }
        catch
        {
            // Frame rusak diabaikan; gambar terakhir tetap tampil.
            return false;
        }
    }
}

public sealed record MonitorOptionViewModel(string Id, string Label, bool IsPrimary);
