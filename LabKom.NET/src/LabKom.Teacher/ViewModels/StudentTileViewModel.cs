using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using LabKom.Shared.Contracts;

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
            _lastFrameStreamId = null;
            _lastFrameSequence = 0;
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
