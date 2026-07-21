using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LabKom.Shared.Contracts;
using LabKom.Teacher.Services;
using LabKom.Teacher;
using Microsoft.Win32;

namespace LabKom.Teacher.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private const int MaxActivityRows = 200;

    private readonly PresenceRegistry _registry;
    private readonly RemoteCommandService _remote;
    private readonly RemoteControlService _remoteControl;
    private readonly WakeOnLanService _wol;
    private readonly TeacherScreenBroadcaster _broadcaster;
    private readonly FileDistributionService _files;
    private readonly FileCollectionService _fileCollection;
    private readonly ActivityFeed _feed;
    private readonly ClassroomGroupStore _groups;
    private readonly ClassroomLessonService _lessons;
    private readonly TeacherAuthorizationService _authorization;
    private readonly TechnicianConsoleService _technician;
    private readonly TelemetryRegistry _telemetry;
    private readonly TelemetrySessionRecorder _telemetryRecorder;
    private readonly DispatcherTimer _telemetryRefreshTimer;
    private bool _suppressMonitorCommand;
    private string[] _blockedDomains = Array.Empty<string>();
    private string[] _blockedProcesses = Array.Empty<string>();
    private readonly Dictionary<string, RemoteControlWindow> _remoteWindows =
        new(StringComparer.OrdinalIgnoreCase);
    private ClassroomConsoleWindow? _classroomWindow;
    private TechnicianConsoleWindow? _technicianWindow;

    [ObservableProperty] private string _serverStatus = "Memulai...";
    [ObservableProperty] private int _onlineCount;
    [ObservableProperty] private int _totalCount;
    [ObservableProperty] private StudentTileViewModel? _selectedStudent;
    [ObservableProperty] private MonitorOptionViewModel? _selectedMonitor;
    [ObservableProperty] private string _broadcastMessage = string.Empty;
    [ObservableProperty] private string _selectedMessage = string.Empty;
    [ObservableProperty] private string _studentSearch = string.Empty;
    [ObservableProperty] private bool _showOfflineStudents = true;
    [ObservableProperty] private bool _isScreenBroadcasting;
    [ObservableProperty] private bool _isScreenBroadcastPaused;
    [ObservableProperty] private string _screenBroadcastTarget = "Semua siswa";
    [ObservableProperty] private int _selectedCount;
    [ObservableProperty]
    private string _selectionStatus = "Belum ada PC dipilih.";
    [ObservableProperty] private string _newGroupName = string.Empty;
    [ObservableProperty] private ClassroomGroupViewModel? _selectedGroup;
    [ObservableProperty]
    private string _policyStatus =
        "Policy situs dan aplikasi belum diaktifkan.";
    [ObservableProperty]
    private string _telemetryStatus = "Telemetry: menunggu sampel Agent...";

    public ObservableCollection<StudentTileViewModel> Students { get; } = new();
    public ObservableCollection<ActivityEntryViewModel> Activities { get; } = new();
    public ObservableCollection<ClassroomGroupViewModel> SavedGroups { get; } = new();
    public ICollectionView StudentsView { get; }

    public string CurrentRole => _authorization.CurrentRole.ToString();
    public string TelemetryFilePath => _telemetryRecorder.SessionFilePath;
    public MainViewModel(
        PresenceRegistry registry,
        RemoteCommandService remote,
        RemoteControlService remoteControl,
        WakeOnLanService wol,
        TeacherScreenBroadcaster broadcaster,
        FileDistributionService files,
        FileCollectionService fileCollection,
        ActivityFeed feed,
        ClassroomGroupStore groups,
        ClassroomLessonService lessons,
        TeacherAuthorizationService authorization,
        TechnicianConsoleService technician,
        TelemetryRegistry telemetry,
        TelemetrySessionRecorder telemetryRecorder)
    {
        _registry = registry;
        _remote = remote;
        _remoteControl = remoteControl;
        _wol = wol;
        _broadcaster = broadcaster;
        _files = files;
        _fileCollection = fileCollection;
        _feed = feed;
        _groups = groups;
        _lessons = lessons;
        _authorization = authorization;
        _technician = technician;
        _telemetry = telemetry;
        _telemetryRecorder = telemetryRecorder;
        _telemetryRefreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(5),
        };
        _telemetryRefreshTimer.Tick += (_, _) => UpdateTelemetryStatus();
        _telemetryRefreshTimer.Start();

        StudentsView = CollectionViewSource.GetDefaultView(Students);
        StudentsView.Filter = FilterStudent;
        StudentsView.SortDescriptions.Add(
            new SortDescription(
                nameof(StudentTileViewModel.PcName),
                ListSortDirection.Ascending));

        _registry.PresenceChanged += OnPresenceChanged;
        _registry.FrameUpdated += OnFrameUpdated;
        _registry.MonitorInventoryUpdated += OnMonitorInventoryUpdated;
        _registry.ChatReceived += OnChatReceived;
        _feed.RecordReceived += OnActivityReceived;
        _feed.FileProgressReceived += OnFileProgressReceived;
        _feed.CommandResultReceived += OnCommandResultReceived;
        _telemetry.TelemetryReceived += OnTelemetryReceived;
        _broadcaster.StateChanged += (_, _) =>
            Application.Current?.Dispatcher.Invoke(ApplyBroadcastState);

        ReloadGroups();

        ServerStatus = "Hub aktif. Menunggu Student Agent...";
    }

    private void OnPresenceChanged(object? sender, PresenceUpdate update) =>
        Application.Current?.Dispatcher.Invoke(() => ApplyPresence(update));
    private void OnTelemetryReceived(
        object? sender,
        DeviceTelemetryUpdate update) =>
        Application.Current?.Dispatcher.Invoke(() => ApplyTelemetry(update));


    private void OnFrameUpdated(object? sender, ScreenFrame frame) =>
        Application.Current?.Dispatcher.Invoke(() => ApplyFrame(frame));

    private void OnMonitorInventoryUpdated(object? sender, MonitorInventory inventory) =>
        Application.Current?.Dispatcher.Invoke(() => ApplyMonitorInventory(inventory));

    private void OnActivityReceived(object? sender, ActivityRecord record) =>
        Application.Current?.Dispatcher.Invoke(() => ApplyActivity(record));

    private void OnChatReceived(object? sender, ChatMessage message) =>
        Application.Current?.Dispatcher.Invoke(() => ApplyChat(message));

    private void OnFileProgressReceived(
        object? sender,
        FileDistributionProgress progress) =>
        Application.Current?.Dispatcher.Invoke(() => ApplyFileProgress(progress));
    private void OnCommandResultReceived(object? sender, CommandResult result) =>
        Application.Current?.Dispatcher.Invoke(() => ApplyCommandResult(result));

    private bool FilterStudent(object item)
    {
        if (item is not StudentTileViewModel student) return false;
        if (!ShowOfflineStudents && student.Status == StudentStatus.Offline) return false;
        if (string.IsNullOrWhiteSpace(StudentSearch)) return true;

        var search = StudentSearch.Trim();
        return student.PcName.Contains(search, StringComparison.OrdinalIgnoreCase)
               || (student.StudentName?.Contains(
                   search,
                   StringComparison.OrdinalIgnoreCase) ?? false)
               || student.IpAddress.Contains(search, StringComparison.OrdinalIgnoreCase);
    }

    private void ApplyBroadcastState()
    {
        IsScreenBroadcasting = _broadcaster.IsActive;
        IsScreenBroadcastPaused = _broadcaster.IsPaused;
        ScreenBroadcastTarget = _broadcaster.AudienceLabel;
    }

    private void ApplyPresence(PresenceUpdate update)
    {
        var existing = Students.FirstOrDefault(student =>
            string.Equals(
                student.PcName,
                update.PcName,
                StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            existing = new StudentTileViewModel { PcName = update.PcName };
            Students.Add(existing);
        }

        existing.Apply(update);
        TotalCount = Students.Count;
        OnlineCount = Students.Count(
            student => student.Status != StudentStatus.Offline);
        StudentsView.Refresh();
        if (ReferenceEquals(existing, SelectedStudent))
        {
            NotifySelectedCommandState();
        }
    }
    private void ApplyTelemetry(DeviceTelemetryUpdate update)
    {
        var tile = Students.FirstOrDefault(student =>
            string.Equals(
                student.PcName,
                update.Snapshot.Telemetry.PcName,
                StringComparison.OrdinalIgnoreCase));
        tile?.ApplyTelemetry(update.Snapshot);

        UpdateTelemetryStatus(update.Summary);
    }

    private void UpdateTelemetryStatus(DeviceTelemetrySummary? current = null)
    {
        var summary = current ?? _telemetry.Summary();
        TelemetryStatus =
            $"Telemetry {summary.ActiveDevices}/{Math.Max(TotalCount, summary.KnownDevices)} aktif"
            + $" | stale {summary.StaleDevices}"
            + $" | warning {summary.WarningDevices} (kritis {summary.CriticalDevices})"
            + $" | p95 {summary.P95LatencyMs:F0} ms"
            + $" | sampel {summary.AcceptedSamples}, ditolak {summary.RejectedSamples}"
            + $" | CSV {_telemetryRecorder.RecordedSamples}, drop {_telemetryRecorder.DroppedSamples}";
    }


    private void ApplyFrame(ScreenFrame frame)
    {
        var tile = Students.FirstOrDefault(student =>
            string.Equals(
                student.PcName,
                frame.PcName,
                StringComparison.OrdinalIgnoreCase));
        tile?.ApplyFrame(frame);
    }

    private void ApplyMonitorInventory(MonitorInventory inventory)
    {
        var tile = Students.FirstOrDefault(student =>
            string.Equals(
                student.PcName,
                inventory.PcName,
                StringComparison.OrdinalIgnoreCase));
        if (tile is null) return;

        var previousMonitorId = ReferenceEquals(tile, SelectedStudent)
            ? SelectedMonitor?.Id
            : null;
        tile.ApplyInventory(inventory);

        if (!ReferenceEquals(tile, SelectedStudent)) return;
        SelectedMonitor = tile.Monitors.FirstOrDefault(monitor =>
                              string.Equals(
                                  monitor.Id,
                                  previousMonitorId,
                                  StringComparison.OrdinalIgnoreCase))
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

    private void ApplyFileProgress(FileDistributionProgress progress)
    {
        Activities.Insert(0, ActivityEntryViewModel.FromFileProgress(progress));
        TrimActivityRows();
    }
    private void ApplyCommandResult(CommandResult result)
    {
        Activities.Insert(0, ActivityEntryViewModel.FromCommandResult(result));
        TrimActivityRows();

        if (result.Kind is RemoteCommandKind.WebFilter or RemoteCommandKind.AppBlock)
        {
            PolicyStatus = $"{result.PcName}: {result.Message ?? result.State.ToString()}";
        }
    }

    private void TrimActivityRows()
    {
        while (Activities.Count > MaxActivityRows)
        {
            Activities.RemoveAt(Activities.Count - 1);
        }
    }

    partial void OnSelectedStudentChanged(
        StudentTileViewModel? oldValue,
        StudentTileViewModel? newValue)
    {
        _suppressMonitorCommand = true;
        try
        {
            SelectedMonitor = newValue?.Monitors.FirstOrDefault(
                                  monitor => monitor.IsPrimary)
                              ?? newValue?.Monitors.FirstOrDefault();
        }
        finally
        {
            _suppressMonitorCommand = false;
        }

        if (newValue is not null)
        {
            _ = _remote.SetCaptureProfileAsync(
                newValue.PcName,
                CaptureProfile.Focus,
                SelectedMonitor?.Id);
        }

        if (oldValue is not null && newValue?.PcName != oldValue.PcName)
        {
            _ = _remote.SetCaptureProfileAsync(
                oldValue.PcName,
                CaptureProfile.Thumbnail);
        }

        NotifySelectedCommandState();
    }

    partial void OnSelectedMonitorChanged(MonitorOptionViewModel? value)
    {
        if (_suppressMonitorCommand || SelectedStudent is null || value is null)
        {
            return;
        }

        _ = _remote.SetCaptureProfileAsync(
            SelectedStudent.PcName,
            CaptureProfile.Focus,
            value.Id);
    }

    partial void OnStudentSearchChanged(string value) => StudentsView.Refresh();

    partial void OnShowOfflineStudentsChanged(bool value) => StudentsView.Refresh();

    partial void OnBroadcastMessageChanged(string value) =>
        BroadcastChatCommand.NotifyCanExecuteChanged();

    partial void OnSelectedMessageChanged(string value) =>
        SendChatSelectedCommand.NotifyCanExecuteChanged();

    partial void OnNewGroupNameChanged(string value) =>
        SaveSelectionAsGroupCommand.NotifyCanExecuteChanged();

    partial void OnSelectedGroupChanged(ClassroomGroupViewModel? value)
    {
        ApplySelectedGroupCommand.NotifyCanExecuteChanged();
        DeleteSelectedGroupCommand.NotifyCanExecuteChanged();
    }

    private bool HasSelectedStudent() => SelectionTargets().Length > 0;
    private bool CanOpenRemote() =>
        SelectedStudent is { Status: not StudentStatus.Offline };

    private bool CanSendSelectedMessage() =>
        HasSelectedStudent() && !string.IsNullOrWhiteSpace(SelectedMessage);

    private bool CanBroadcastMessage() =>
        !string.IsNullOrWhiteSpace(BroadcastMessage);

    private bool CanSaveSelectionAsGroup() =>
        HasSelectedStudent() && !string.IsNullOrWhiteSpace(NewGroupName);

    private bool HasSelectedGroup() => SelectedGroup is not null;

    private string[] SelectionTargets() =>
        Students
            .Where(student => student.IsSelected)
            .Select(student => student.PcName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(pcName => pcName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private void NotifySelectedCommandState()
    {
        LockSelectedCommand.NotifyCanExecuteChanged();
        UnlockSelectedCommand.NotifyCanExecuteChanged();
        ShutdownSelectedCommand.NotifyCanExecuteChanged();
        RestartSelectedCommand.NotifyCanExecuteChanged();
        LogOffSelectedCommand.NotifyCanExecuteChanged();
        WakeSelectedCommand.NotifyCanExecuteChanged();
        StartScreenBroadcastSelectedCommand.NotifyCanExecuteChanged();
        ShareFileToSelectedCommand.NotifyCanExecuteChanged();
        CollectFileFromSelectedCommand.NotifyCanExecuteChanged();
        SendChatSelectedCommand.NotifyCanExecuteChanged();
        SaveSelectionAsGroupCommand.NotifyCanExecuteChanged();
        OpenRemoteCommand.NotifyCanExecuteChanged();
    }

    private void UpdateSelectionState()
    {
        var selected = Students
            .Where(student => student.IsSelected)
            .ToArray();
        SelectedCount = selected.Length;
        SelectionStatus = selected.Length switch
        {
            0 => "Belum ada PC dipilih.",
            1 => $"Dipilih: {selected[0].PcName}",
            _ => $"{selected.Length} PC dipilih untuk aksi grup.",
        };

        if (SelectedStudent is not null
            && !SelectedStudent.IsSelected)
        {
            SelectedStudent = selected.FirstOrDefault();
        }

        NotifySelectedCommandState();
    }

    private void ReloadGroups(string? selectedName = null)
    {
        var desiredName = selectedName ?? SelectedGroup?.Name;
        SavedGroups.Clear();
        foreach (var group in _groups.Snapshot())
        {
            SavedGroups.Add(ClassroomGroupViewModel.From(group));
        }

        SelectedGroup = SavedGroups.FirstOrDefault(group =>
            string.Equals(
                group.Name,
                desiredName,
                StringComparison.OrdinalIgnoreCase));
    }

    [RelayCommand]
    private void SelectStudent(StudentTileViewModel? tile)
    {
        if (tile is null) return;

        var additive =
            (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
        if (!additive)
        {
            foreach (var student in Students)
            {
                student.IsSelected = ReferenceEquals(student, tile);
            }

            SelectedStudent = tile;
        }
        else
        {
            tile.IsSelected = !tile.IsSelected;
            if (tile.IsSelected)
            {
                SelectedStudent = tile;
            }
            else if (ReferenceEquals(SelectedStudent, tile))
            {
                SelectedStudent = Students.FirstOrDefault(
                    student => student.IsSelected);
            }
        }

        UpdateSelectionState();
    }

    [RelayCommand]
    private void SelectAllOnline()
    {
        foreach (var student in Students)
        {
            student.IsSelected = student.Status != StudentStatus.Offline;
        }

        SelectedStudent = Students.FirstOrDefault(
            student => student.IsSelected);
        UpdateSelectionState();
    }

    [RelayCommand]
    private void ClearSelection()
    {
        foreach (var student in Students)
        {
            student.IsSelected = false;
        }

        SelectedStudent = null;
        UpdateSelectionState();
    }

    [RelayCommand(CanExecute = nameof(HasSelectedGroup))]
    private void ApplySelectedGroup()
    {
        var group = SelectedGroup!;
        var members = group.PcNames.ToHashSet(
            StringComparer.OrdinalIgnoreCase);
        foreach (var student in Students)
        {
            student.IsSelected = members.Contains(student.PcName);
        }

        SelectedStudent = Students
            .Where(student => student.IsSelected)
            .OrderByDescending(student =>
                student.Status != StudentStatus.Offline)
            .ThenBy(student => student.PcName)
            .FirstOrDefault();
        UpdateSelectionState();

        var visibleMembers = SelectionTargets().Length;
        SelectionStatus =
            $"Grup {group.Name}: {visibleMembers}/{group.PcNames.Count} PC tersedia.";
    }

    [RelayCommand(CanExecute = nameof(CanSaveSelectionAsGroup))]
    private void SaveSelectionAsGroup()
    {
        try
        {
            var group = _groups.Upsert(
                NewGroupName,
                SelectionTargets());
            ReloadGroups(group.Name);
            NewGroupName = string.Empty;
            SelectionStatus =
                $"Grup {group.Name} disimpan ({group.PcNames.Count} PC).";
        }
        catch (Exception ex) when (
            ex is ArgumentException
                or InvalidOperationException
                or IOException
                or UnauthorizedAccessException)
        {
            MessageBox.Show(
                ex.Message,
                "Grup kelas gagal disimpan",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelectedGroup))]
    private void DeleteSelectedGroup()
    {
        var group = SelectedGroup!;
        if (MessageBox.Show(
                $"Hapus grup '{group.Name}'?",
                "Hapus Grup Kelas",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No) != MessageBoxResult.Yes)
        {
            return;
        }

        _groups.Delete(group.Name);
        ReloadGroups();
        SelectionStatus = $"Grup {group.Name} dihapus.";
    }
    [RelayCommand]
    private void OpenClassroomConsole()
    {
        if (_classroomWindow is not null)
        {
            _ = _classroomWindow.Activate();
            return;
        }

        _classroomWindow = new ClassroomConsoleWindow(
            _lessons,
            SelectionTargets())
        {
            Owner = Application.Current.MainWindow,
        };
        _classroomWindow.Closed += (_, _) =>
            _classroomWindow = null;
        _classroomWindow.Show();
    }


    [RelayCommand]
    private void OpenTechnicianConsole()
    {
        _authorization.Demand(
            LabKom.Shared.Security.TeacherPermission.TechnicianConsole,
            "technician.open",
            null);
        if (_technicianWindow is not null)
        {
            _ = _technicianWindow.Activate();
            return;
        }

        _technicianWindow = new TechnicianConsoleWindow(_technician)
        {
            Owner = Application.Current.MainWindow,
        };
        _technicianWindow.Closed += (_, _) =>
            _technicianWindow = null;
        _technicianWindow.Show();
    }


    [RelayCommand(CanExecute = nameof(CanOpenRemote))]
    private void OpenRemote()
    {
        var student = SelectedStudent;
        if (student is null) return;
        if (_remoteWindows.TryGetValue(student.PcName, out var existing))
        {
            _ = existing.Activate();
            return;
        }

        var window = new RemoteControlWindow(
            _registry,
            _remoteControl,
            _remote,
            student.PcName,
            SelectedMonitor?.Id)
        {
            Owner = Application.Current.MainWindow,
        };
        _remoteWindows[student.PcName] = window;
        window.Closed += (_, _) =>
            _remoteWindows.Remove(student.PcName);
        window.Show();
    }

    [RelayCommand]
    private async Task LockAll() =>
        await _remote.LockAsync(null, "Mohon perhatian ke instruktur");

    [RelayCommand]
    private async Task UnlockAll() => await _remote.UnlockAsync(null);

    [RelayCommand(CanExecute = nameof(HasSelectedStudent))]
    private async Task LockSelected()
    {
        var targets = SelectionTargets();
        await Task.WhenAll(targets.Select(pcName =>
            _remote.LockAsync(pcName)));
    }

    [RelayCommand(CanExecute = nameof(HasSelectedStudent))]
    private async Task UnlockSelected()
    {
        var targets = SelectionTargets();
        await Task.WhenAll(targets.Select(pcName =>
            _remote.UnlockAsync(pcName)));
    }

    [RelayCommand(CanExecute = nameof(HasSelectedStudent))]
    private async Task ShutdownSelected()
    {
        var targets = SelectionTargets();
        if (!ConfirmPower(
                $"Matikan {targets.Length} PC terpilih?",
                "Shutdown PC Terpilih"))
        {
            return;
        }

        await Task.WhenAll(targets.Select(pcName =>
            _remote.ShutdownAsync(pcName, 5)));
    }

    [RelayCommand(CanExecute = nameof(HasSelectedStudent))]
    private async Task RestartSelected()
    {
        var targets = SelectionTargets();
        if (!ConfirmPower(
                $"Restart {targets.Length} PC terpilih?",
                "Restart PC Terpilih"))
        {
            return;
        }

        await Task.WhenAll(targets.Select(pcName =>
            _remote.RestartAsync(pcName, 5)));
    }

    [RelayCommand(CanExecute = nameof(HasSelectedStudent))]
    private async Task LogOffSelected()
    {
        var targets = SelectionTargets();
        if (!ConfirmPower(
                $"Logoff pengguna pada {targets.Length} PC terpilih?",
                "Logoff PC Terpilih"))
        {
            return;
        }

        await Task.WhenAll(targets.Select(pcName =>
            _remote.LogOffAsync(pcName)));
    }
    [RelayCommand]
    private async Task ShutdownAll()
    {
        if (!ConfirmPower(
                "Matikan semua PC siswa dalam 30 detik?",
                "Shutdown Semua PC"))
        {
            return;
        }

        await _remote.ShutdownAsync(null, 30);
    }

    [RelayCommand]
    private async Task LogOffAll()
    {
        if (!ConfirmPower(
                "Logoff semua pengguna siswa sekarang?",
                "Logoff Semua PC"))
        {
            return;
        }

        await _remote.LogOffAsync(null);
    }

    [RelayCommand(CanExecute = nameof(HasSelectedStudent))]
    private void WakeSelected()
    {
        var selected = Students
            .Where(student => student.IsSelected)
            .ToArray();
        var failed = selected.Count(student =>
            string.IsNullOrWhiteSpace(student.MacAddress)
            || !_wol.TryWake(student.MacAddress));
        if (failed > 0)
        {
            MessageBox.Show(
                $"{failed} dari {selected.Length} PC gagal dikirimi Wake-on-LAN. " +
                "Pastikan MAC address tersedia dan WOL aktif.",
                "Wake-on-LAN",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }
    [RelayCommand(CanExecute = nameof(CanBroadcastMessage))]
    private async Task BroadcastChat()
    {
        await _remote.BroadcastChatAsync(BroadcastMessage);
        BroadcastMessage = string.Empty;
    }

    [RelayCommand(CanExecute = nameof(CanSendSelectedMessage))]
    private async Task SendChatSelected()
    {
        var targets = SelectionTargets();
        var body = SelectedMessage;
        await Task.WhenAll(targets.Select(pcName =>
            _remote.SendChatAsync(pcName, body)));
        SelectedMessage = string.Empty;
    }

    [RelayCommand]
    private async Task ToggleScreenBroadcast()
    {
        if (_broadcaster.IsActive)
        {
            await _broadcaster.StopAsync();
        }
        else
        {
            await _broadcaster.StartAsync(targetPcName: null);
        }
    }

    [RelayCommand]
    private async Task StartScreenBroadcastAll()
    {
        if (_broadcaster.IsActive) await _broadcaster.StopAsync();
        await _broadcaster.StartAsync(targetPcName: null);
    }

    [RelayCommand]
    private async Task StopScreenBroadcast() => await _broadcaster.StopAsync();

    [RelayCommand]
    private async Task ToggleScreenBroadcastPause() =>
        await _broadcaster.TogglePauseAsync();

    [RelayCommand(CanExecute = nameof(HasSelectedStudent))]
    private async Task StartScreenBroadcastSelected()
    {
        var targets = SelectionTargets();
        if (_broadcaster.IsActive) await _broadcaster.StopAsync();
        await _broadcaster.StartForTargetsAsync(targets);
    }

    [RelayCommand]
    private async Task ShareFileToAll()
    {
        var path = SelectFile("Pilih file untuk dibagikan ke semua siswa");
        if (path is not null)
        {
            await _files.ShareFileAsync(path, targetPcName: null);
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelectedStudent))]
    private async Task ShareFileToSelected()
    {
        var targets = SelectionTargets();
        var path = SelectFile(
            $"Pilih file untuk dibagikan ke {targets.Length} PC terpilih");
        if (path is not null)
        {
            await _files.ShareFileToTargetsAsync(path, targets);
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelectedStudent))]
    private async Task CollectFileFromSelected()
    {
        var selection = FileCollectDialog.Show(Application.Current.MainWindow);
        if (selection is null) return;

        var targets = SelectionTargets();
        await Task.WhenAll(targets.Select(pcName =>
            _fileCollection.RequestAsync(
                pcName,
                selection.Root,
                selection.RelativePath)));
        ServerStatus =
            $"Permintaan file collect dikirim ke {targets.Length} PC. " +
            $"Hasil: {_fileCollection.CollectedFilesPath}";
    }


    [RelayCommand]
    private async Task RefreshStudentViews()
    {
        var tasks = Students
            .Where(student => student.Status != StudentStatus.Offline)
            .Select(student => _remote.SetCaptureProfileAsync(
                student.PcName,
                ReferenceEquals(student, SelectedStudent)
                    ? CaptureProfile.Focus
                    : CaptureProfile.Thumbnail,
                ReferenceEquals(student, SelectedStudent)
                    ? SelectedMonitor?.Id
                    : null));
        await Task.WhenAll(tasks);
        ServerStatus =
            $"Refresh dikirim ke {OnlineCount} PC pada {DateTime.Now:HH:mm:ss}.";
    }

    [RelayCommand]
    private void ClearActivities() => Activities.Clear();

    [RelayCommand]
    private async Task ConfigureWebFilter()
    {
        var values = ListPolicyDialog.ShowEditor(
            "Blokir Situs",
            "Masukkan satu domain per baris. URL lengkap akan dinormalisasi menjadi domain.",
            "Contoh:\nyoutube.com\nfacebook.com",
            _blockedDomains);
        if (values is null) return;

        try
        {
            await _remote.ApplyWebFilterAsync(values);
            _blockedDomains = values.ToArray();
            PolicyStatus = $"Blokir situs aktif: {_blockedDomains.Length} domain.";
        }
        catch (ArgumentException ex)
        {
            ShowPolicyError(ex.Message);
        }
    }

    [RelayCommand]
    private async Task DisableWebFilter()
    {
        await _remote.DisableWebFilterAsync();
        _blockedDomains = Array.Empty<string>();
        PolicyStatus = "Blokir situs dinonaktifkan.";
    }

    [RelayCommand]
    private async Task ConfigureAppBlock()
    {
        var values = ListPolicyDialog.ShowEditor(
            "Blokir Aplikasi",
            "Masukkan nama proses per baris, tanpa path. Akhiran .exe boleh ditulis.",
            "Contoh:\nchrome\nsteam.exe\ndiscord",
            _blockedProcesses);
        if (values is null) return;

        try
        {
            await _remote.ApplyAppBlockAsync(values);
            _blockedProcesses = values.ToArray();
            PolicyStatus =
                $"Blokir aplikasi aktif: {_blockedProcesses.Length} proses.";
        }
        catch (ArgumentException ex)
        {
            ShowPolicyError(ex.Message);
        }
    }

    [RelayCommand]
    private async Task DisableAppBlock()
    {
        await _remote.DisableAppBlockAsync();
        _blockedProcesses = Array.Empty<string>();
        PolicyStatus = "Blokir aplikasi dinonaktifkan.";
    }

    [RelayCommand]
    private void ExitApplication() => Application.Current.Shutdown();

    [RelayCommand]
    private void ShowAbout() =>
        MessageBox.Show(
            "LabKom Teacher Console\nNative Windows classroom management\n\n" +
            "Fitur aktif: monitoring, attention lock, broadcast layar, chat, " +
            "distribusi file, power, Wake-on-LAN, serta policy situs/aplikasi.",
            "Tentang LabKom",
            MessageBoxButton.OK,
            MessageBoxImage.Information);

    private static string? SelectFile(string title)
    {
        var dialog = new OpenFileDialog
        {
            Title = title,
            Multiselect = false,
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    private static bool ConfirmPower(string message, string title) =>
        MessageBox.Show(
            message,
            title,
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No) == MessageBoxResult.Yes;

    private static void ShowPolicyError(string message) =>
        MessageBox.Show(
            message,
            "Policy tidak valid",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
}