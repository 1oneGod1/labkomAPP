using System.IO;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using LabKom.Shared.Contracts;

namespace LabKom.Teacher.ViewModels;

public partial class StudentTileViewModel : ObservableObject
{
    [ObservableProperty] private string _pcName = "";
    [ObservableProperty] private string _ipAddress = "";
    [ObservableProperty] private string _macAddress = "";
    [ObservableProperty] private string? _studentName;
    [ObservableProperty] private StudentStatus _status;
    [ObservableProperty] private DateTime _lastSeen;
    [ObservableProperty] private BitmapImage? _thumbnail;
    [ObservableProperty] private bool _isSelected;

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
        if (update.Snapshot is { } snap)
        {
            IpAddress = snap.IpAddress;
            MacAddress = snap.MacAddress;
            StudentName = snap.StudentName;
        }
        if (Status == StudentStatus.Offline)
        {
            Thumbnail = null;
        }
    }

    public void ApplyFrame(ScreenFrame frame)
    {
        if (frame.JpegData.Length == 0) return;
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = new MemoryStream(frame.JpegData);
            bmp.EndInit();
            bmp.Freeze();
            Thumbnail = bmp;
        }
        catch
        {
            // Frame corrupt — abaikan, frame berikutnya akan menggantikan.
        }
    }
}
