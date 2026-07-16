using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LabKom.Shared.Contracts;
using LabKom.Teacher.Services;
using Microsoft.Win32;

namespace LabKom.Teacher.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly PresenceRegistry _registry;
    private readonly RemoteCommandService _remote;
    private readonly WakeOnLanService _wol;
    private readonly TeacherScreenBroadcaster _broadcaster;
    private readonly FileDistributionService _files;
    private readonly ActivityFeed _feed;
    private bool _suppressMonitorCommand;

    private const int MaxActivityRows = 200;

    [ObservableProperty] private string _serverStatus = "Memulai…";
    [ObservableProperty] private int _onlineCount;
    [ObservableProperty] private int _totalCount;
    [ObservableProperty] private StudentTileViewModel? _selectedStudent;
    [ObservableProperty] private MonitorOptionViewModel? _selectedMonitor;
    [ObservableProperty] private string _broadcastMessage = "";
    [ObservableProperty] private bool _isScreenBroadcasting;
    [ObservableProperty] private bool _isScreenBroadcastPaused;
    [ObservableProperty] private string _screenBroadcastTarget = "Semua siswa";

    public ObservableCollection<StudentTileViewModel> Students { get; } = new();
    public ObservableCollection<ActivityEntryViewModel> Activities { get; } = new();

    public MainViewModel(
        PresenceRegistry registry,
        RemoteCommandService remote,
        WakeOnLanService wol,
        TeacherScreenBroadcaster broadcaster,
        FileDistributionService files,
        ActivityFeed feed)
    {
        _registry = registry;
        _remote = remote;
        _wol = wol;
        _broadcaster = broadcaster;
        _files = files;
        _feed = feed;

        _registry.PresenceChanged += OnPresenceChanged;
        _registry.FrameUpdated += OnFrameUpdated;
        _registry.MonitorInventoryUpdated += OnMonitorInventoryUpdated;
        _registry.ChatReceived += OnChatReceived;
        _feed.RecordReceived += OnActivityReceived;
        _feed.CommandResultReceived += OnCommandResultReceived;
        _broadcaster.StateChanged += (_, _) =>
            Application.Current?.Dispatcher.Invoke(ApplyBroadcastState);

        ServerStatus = "Hub aktif. Menunggu Student Agent…";
    }

    private void OnPresenceChanged(object? sender, PresenceUpdate update) =>
        Application.Current?.Dispatcher.Invoke(() => ApplyPresence(update));

    private void OnFrameUpdated(object? sender, ScreenFrame frame) =>
        Application.Current?.Dispatcher.Invoke(() => ApplyFrame(frame));

    private void OnMonitorInventoryUpdated(object? sender, MonitorInventory inventory) =>
        Application.Current?.Dispatcher.Invoke(() => ApplyMonitorInventory(inventory));

    private void OnActivityReceived(object? sender, ActivityRecord record) =>
        Application.Current?.Dispatcher.Invoke(() => ApplyActivity(record));

    private void OnChatReceived(object? sender, ChatMessage message) =>
        Application.Current?.Dispatcher.Invoke(() => ApplyChat(message));

    private void OnCommandResultReceived(object? sender, CommandResult result) =>
        Application.Current?.Dispatcher.Invoke(() => ApplyCommandResult(result));

    private void ApplyBroadcastState()
    {
        IsScreenBroadcasting = _broadcaster.IsActive;
        IsScreenBroadcastPaused = _broadcaster.IsPaused;
        ScreenBroadcastTarget = _broadcaster.TargetPcName is null
            ? "Semua siswa"
            : _broadcaster.TargetPcName;
    }

    private void ApplyPresence(PresenceUpdate update)
    {
        var existing = Students.FirstOrDefault(s =>
            string.Equals(s.PcName, update.PcName, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            existing = new StudentTileViewModel { PcName = update.PcName };
            Students.Add(existing);
        }
        existing.Apply(update);
        TotalCount = Students.Count;
        OnlineCount = Students.Count(s => s.Status != StudentStatus.Offline);
    }

    private void ApplyFrame(ScreenFrame frame)
    {
        var tile = Students.FirstOrDefault(s =>
            string.Equals(s.PcName, frame.PcName, StringComparison.OrdinalIgnoreCase));
        tile?.ApplyFrame(frame);
    }

    private void ApplyMonitorInventory(MonitorInventory inventory)
    {
        var tile = Students.FirstOrDefault(student =>
            string.Equals(student.PcName, inventory.PcName, StringComparison.OrdinalIgnoreCase));
        if (tile is null) return;

        var previousMonitorId = ReferenceEquals(tile, SelectedStudent)
            ? SelectedMonitor?.Id
            : null;
        tile.ApplyInventory(inventory);

        if (!ReferenceEquals(tile, SelectedStudent)) return;
        SelectedMonitor = tile.Monitors.FirstOrDefault(monitor =>
                              string.Equals(monitor.Id, previousMonitorId, StringComparison.OrdinalIgnoreCase))
                          ?? tile.Monitors.FirstOrDefault(monitor => monitor.IsPrimary)
                          ?? tile.Monitors.FirstOrDefault();
    }

    private void ApplyActivity(ActivityRecord record)
    {
        Activities.Insert(0, ActivityEntryViewModel.From(record));
        TrimActivityRows();
    }

    private void ApplyChat(ChatMessage message)
    {
        Activities.Insert(0, ActivityEntryViewModel.FromChat(message));
        TrimActivityRows();
    }

    private void ApplyCommandResult(CommandResult result)
    {
        Activities.Insert(0, ActivityEntryViewModel.FromCommandResult(result));
        TrimActivityRows();
    }

    private void TrimActivityRows()
    {
        while (Activities.Count > MaxActivityRows)
        {
            Activities.RemoveAt(Activities.Count - 1);
        }
    }

    partial void OnSelectedStudentChanged(StudentTileViewModel? oldValue, StudentTileViewModel? newValue)
    {
        if (oldValue is not null) oldValue.IsSelected = false;

        _suppressMonitorCommand = true;
        try
        {
            SelectedMonitor = newValue?.Monitors.FirstOrDefault(monitor => monitor.IsPrimary)
                              ?? newValue?.Monitors.FirstOrDefault();
        }
        finally
        {
            _suppressMonitorCommand = false;
        }

        if (newValue is not null)
        {
            newValue.IsSelected = true;
            _ = _remote.SetCaptureProfileAsync(
                newValue.PcName,
                CaptureProfile.Focus,
                SelectedMonitor?.Id);
        }

        if (oldValue is not null && newValue?.PcName != oldValue.PcName)
        {
            _ = _remote.SetCaptureProfileAsync(oldValue.PcName, CaptureProfile.Thumbnail);
        }
    }

    partial void OnSelectedMonitorChanged(MonitorOptionViewModel? value)
    {
        if (_suppressMonitorCommand || SelectedStudent is null || value is null) return;
        _ = _remote.SetCaptureProfileAsync(
            SelectedStudent.PcName,
            CaptureProfile.Focus,
            value.Id);
    }

    [RelayCommand]
    private void SelectStudent(StudentTileViewModel? tile) => SelectedStudent = tile;

    [RelayCommand]
    private async Task LockAll() => await _remote.LockAsync(null, "Mohon perhatian ke instruktur");

    [RelayCommand]
    private async Task UnlockAll() => await _remote.UnlockAsync(null);

    [RelayCommand]
    private async Task LockSelected()
    {
        if (SelectedStudent is null) return;
        await _remote.LockAsync(SelectedStudent.PcName);
    }

    [RelayCommand]
    private async Task UnlockSelected()
    {
        if (SelectedStudent is null) return;
        await _remote.UnlockAsync(SelectedStudent.PcName);
    }

    [RelayCommand]
    private async Task ShutdownSelected()
    {
        if (SelectedStudent is null) return;
        await _remote.ShutdownAsync(SelectedStudent.PcName, 5);
    }

    [RelayCommand]
    private async Task RestartSelected()
    {
        if (SelectedStudent is null) return;
        await _remote.RestartAsync(SelectedStudent.PcName, 5);
    }

    [RelayCommand]
    private async Task ShutdownAll() => await _remote.ShutdownAsync(null, 30);

    [RelayCommand]
    private void WakeSelected()
    {
        if (SelectedStudent is null || string.IsNullOrWhiteSpace(SelectedStudent.MacAddress)) return;
        _wol.TryWake(SelectedStudent.MacAddress);
    }

    [RelayCommand]
    private async Task BroadcastChat()
    {
        if (string.IsNullOrWhiteSpace(BroadcastMessage)) return;
        await _remote.BroadcastChatAsync(BroadcastMessage);
        BroadcastMessage = "";
    }

    [RelayCommand]
    private async Task ToggleScreenBroadcast()
    {
        if (_broadcaster.IsActive) await _broadcaster.StopAsync();
        else await _broadcaster.StartAsync(targetPcName: null);
    }

    [RelayCommand]
    private async Task ToggleScreenBroadcastPause()
    {
        await _broadcaster.TogglePauseAsync();
    }

    [RelayCommand]
    private async Task StartScreenBroadcastSelected()
    {
        if (SelectedStudent is null) return;
        if (_broadcaster.IsActive)
        {
            await _broadcaster.StopAsync();
        }

        await _broadcaster.StartAsync(SelectedStudent.PcName);
    }

    [RelayCommand]
    private async Task ShareFileToAll()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Pilih file untuk dibagikan ke semua siswa",
            Multiselect = false,
        };
        if (dialog.ShowDialog() != true) return;
        await _files.ShareFileAsync(dialog.FileName, targetPcName: null);
    }

    [RelayCommand]
    private async Task ShareFileToSelected()
    {
        if (SelectedStudent is null) return;
        var dialog = new OpenFileDialog
        {
            Title = $"Pilih file untuk dibagikan ke {SelectedStudent.PcName}",
            Multiselect = false,
        };
        if (dialog.ShowDialog() != true) return;
        await _files.ShareFileAsync(dialog.FileName, targetPcName: SelectedStudent.PcName);
    }
}
