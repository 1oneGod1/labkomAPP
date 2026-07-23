using System.Windows;
using LabKom.Shared.Contracts;
using LabKom.Shared.Devices;
using LabKom.Shared.Discovery;
using LabKom.Shared.Hub;
using LabKom.Shared.Security;
using LabKom.Student.Desktop.Services;
using LabKom.Student.Desktop.Services.Capture;
using LabKom.Student.Desktop.Workers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace LabKom.Student.Desktop;

public partial class App : Application
{
    private IHost? _host;
    private DesktopHubClient? _hub;
    private AttentionRecoveryState? _recoveryState;
    private AttentionRecoveryWorker? _recoveryWorker;
    private OverlayWindow? _overlay;
    private BroadcastWindow? _broadcast;
    private ChatWindow? _chat;
    private LessonWindow? _lesson;
    private KeyboardHook? _hook;
    private KeyboardActivityMeter? _keyboardActivity;
    private RemoteSessionController? _remoteSessions;
    private RemoteEmergencyHotkey? _remoteHotkey;
    private RemoteSessionBanner? _remoteBanner;
    private readonly List<ChatMessage> _chatHistory = new();
    private bool _attentionActive;
    private bool _broadcastActive;
    private bool _broadcastPaused;
    private bool _hasUnreadChat;
    private string? _broadcastId;
    private long _lastTeacherFrameSequence;
    private byte[]? _latestTeacherFrame;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (!SingleInstance.TryAcquire("Local\\LabKomStudentDesktop"))
        {
            Shutdown();
            return;
        }

        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Local.json", optional: true)
            .AddEnvironmentVariables();

        string? sharedSecret;
        try
        {
            sharedSecret = ProvisionedSecretStore.Resolve(
                builder.Configuration["Desktop:SharedSecret"]);
        }
        catch (UnauthorizedAccessException)
        {
            sharedSecret = builder.Configuration["Desktop:SharedSecret"];
        }
        var authentication = DeviceCredentialStore.Resolve(sharedSecret);
        if (!HubSecurity.IsStrongSecret(authentication.Secret))
        {
            MessageBox.Show(
                $"LABKOM_SHARED_SECRET wajib diisi minimal {HubSecurity.MinimumSecretLength} karakter.",
                "Konfigurasi LabKom Student belum lengkap",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(-1);
            return;
        }
        builder.Configuration["Desktop:SharedSecret"] = sharedSecret;

        builder.Services.AddSingleton<MachineIdentity>();
        builder.Services.AddSingleton<TeacherEndpointStore>();
        builder.Services.AddSingleton<CaptureProfileState>();
        builder.Services.AddSingleton<GdiScreenCapture>();
        builder.Services.AddSingleton<DxgiScreenCapture>();
        builder.Services.AddSingleton<IScreenCaptureSource>(provider =>
        {
            var preferGdi = builder.Configuration.GetValue<bool>("Capture:PreferGdiCapture", true);
            if (preferGdi)
            {
                return provider.GetRequiredService<GdiScreenCapture>();
            }
            return provider.GetRequiredService<DxgiScreenCapture>();
        });
        builder.Services.AddSingleton<AdaptiveStreamController>();
        builder.Services.AddSingleton<ActivityMonitor>();
        builder.Services.AddSingleton<KeyboardActivityMeter>();
        builder.Services.AddSingleton<FileDownloader>();
        builder.Services.AddSingleton<FileCollectionClient>();
        builder.Services.AddSingleton<DesktopHubClient>();
        builder.Services.AddSingleton<AttentionRecoveryState>();
        builder.Services.AddSingleton<AttentionRecoveryWorker>();
        builder.Services.AddSingleton<RemoteSessionController>();
        builder.Services.AddSingleton<RemoteInputExecutor>();
        builder.Services.AddSingleton<RemoteEmergencyHotkey>();
        builder.Services.AddHostedService(provider =>
            provider.GetRequiredService<AttentionRecoveryWorker>());
        builder.Services.AddHostedService<DiscoveryListener>();
        builder.Services.AddHostedService<DesktopConnectionWorker>();
        builder.Services.AddHostedService<ScreenStreamWorker>();
        builder.Services.AddHostedService<ActivityWorker>();
        builder.Services.AddHostedService<RemoteSessionExpiryWorker>();

        _host = builder.Build();

        _hub = _host.Services.GetRequiredService<DesktopHubClient>();
        _hub.ClassroomStateReceived += OnClassroomStateReceived;
        _hub.AttentionReceived += OnAttentionReceived;
        _hub.BroadcastSignalReceived += OnBroadcastSignalReceived;
        _hub.TeacherFrameReceived += OnTeacherFrameReceived;
        _hub.ChatReceived += OnChatReceived;
        _hub.LessonReceived += OnLessonReceived;
        _recoveryState = _host.Services.GetRequiredService<AttentionRecoveryState>();
        _recoveryWorker = _host.Services.GetRequiredService<AttentionRecoveryWorker>();
        _recoveryWorker.RecoveryTriggered += OnRecoveryTriggered;
        _remoteSessions = _host.Services.GetRequiredService<RemoteSessionController>();
        _remoteSessions.StatusChanged += OnRemoteSessionStatusChanged;
        _remoteHotkey = _host.Services.GetRequiredService<RemoteEmergencyHotkey>();
        _remoteHotkey.Pressed += OnRemoteEmergencyRelease;


        _hook = new KeyboardHook();
        _keyboardActivity = _host.Services
            .GetRequiredService<KeyboardActivityMeter>();
        _keyboardActivity.Start();
        await _host.StartAsync();
    }

    private void OnLessonReceived(
        object? sender,
        LessonSnapshot lesson) =>
        Dispatcher.Invoke(() =>
        {
            if (lesson.Phase == LessonPhase.Ended)
            {
                _lesson?.Apply(lesson);
                return;
            }

            if (_lesson is null)
            {
                _lesson = new LessonWindow(
                    Environment.MachineName,
                    lesson,
                    submission => _hub!.SubmitRegistrationAsync(submission),
                    submission => _hub!.SubmitAssessmentAsync(submission));
                _lesson.Closed += (_, _) => _lesson = null;
                _lesson.Show();
            }
            else
            {
                _lesson.Apply(lesson);
            }

            _ = _lesson.Activate();
        });


    private void OnClassroomStateReceived(
        object? sender,
        ClassroomStateSnapshot snapshot) =>
        Dispatcher.Invoke(() =>
        {
            if (IsEmergencyUnlockActive())
            {
                ReleaseManagedUi();
                return;
            }
            _attentionActive = snapshot.Attention?.Enabled == true;
            _recoveryState?.SetAttention(
                _attentionActive,
                snapshot.Attention?.CommandId,
                DateTimeOffset.UtcNow);

            if (snapshot.Broadcast is { Active: true } broadcast)
            {
                if (!string.Equals(
                        _broadcastId,
                        broadcast.BroadcastId,
                        StringComparison.Ordinal))
                {
                    _lastTeacherFrameSequence = 0;
                    _latestTeacherFrame = null;
                }

                _broadcastActive = true;
                _broadcastPaused = broadcast.Paused;
                _broadcastId = broadcast.BroadcastId;
            }
            else
            {
                _broadcastActive = false;
                _broadcastPaused = false;
                _broadcastId = null;
                _lastTeacherFrameSequence = 0;
                _latestTeacherFrame = null;
            }

            if (_attentionActive)
            {
                CloseBroadcastWindow();
                CloseChatWindow();
                ShowOverlay(snapshot.Attention!.Message);
            }
            else
            {
                CloseOverlayWindow();
                if (_broadcastActive)
                {
                    CloseChatWindow();
                    ShowBroadcastWindow();
                }
                else
                {
                    CloseBroadcastWindow();
                }
            }

            UpdateHookState();
        });
    private void OnAttentionReceived(object? sender, AttentionCommand command) =>
        Dispatcher.Invoke(() =>
        {
            if (command.Enabled
                && EmergencyUnlockStore.TryGetActive(
                    DateTimeOffset.UtcNow,
                    out var emergency))
            {
                ReleaseManagedUi();
                _ = _hub?.ReportCommandResultAsync(
                    command.CommandId,
                    RemoteCommandKind.Attention,
                    CommandExecutionState.Rejected,
                    $"Emergency unlock admin aktif sampai {emergency.ExpiresAtUtc:O}");
                return;
            }
            _attentionActive = command.Enabled;
            _recoveryState?.SetAttention(
                command.Enabled,
                command.CommandId,
                DateTimeOffset.UtcNow);
            if (_attentionActive)
            {
                CloseBroadcastWindow();
                CloseChatWindow();
                ShowOverlay(command.Message);
            }
            else
            {
                CloseOverlayWindow();
                if (_broadcastActive) ShowBroadcastWindow();
            }

            UpdateHookState();
            _ = _hub?.ReportCommandResultAsync(
                command.CommandId,
                RemoteCommandKind.Attention,
                CommandExecutionState.Applied,
                command.Enabled ? "Attention aktif" : "Attention nonaktif");
        });

    private void OnBroadcastSignalReceived(object? sender, TeacherBroadcastSignal signal) =>
        Dispatcher.Invoke(() =>
        {
            if (IsEmergencyUnlockActive())
            {
                ReleaseManagedUi();
                return;
            }
            if (!signal.Active)
            {
                if (!string.Equals(_broadcastId, signal.BroadcastId, StringComparison.Ordinal)) return;

                _broadcastActive = false;
                _broadcastPaused = false;
                _broadcastId = null;
                _lastTeacherFrameSequence = 0;
                _latestTeacherFrame = null;
                CloseBroadcastWindow();
                UpdateHookState();
                return;
            }

            if (!string.Equals(_broadcastId, signal.BroadcastId, StringComparison.Ordinal))
            {
                _broadcastId = signal.BroadcastId;
                _lastTeacherFrameSequence = 0;
                _latestTeacherFrame = null;
            }

            _broadcastActive = true;
            _broadcastPaused = signal.Paused;
            if (!_attentionActive)
            {
                CloseChatWindow();
                ShowBroadcastWindow();
                _broadcast?.SetPaused(_broadcastPaused);
            }

            UpdateHookState();
        });

    private void OnTeacherFrameReceived(object? sender, TeacherFrame frame) =>
        Dispatcher.Invoke(() =>
        {
            if (IsEmergencyUnlockActive())
            {
                ReleaseManagedUi();
                return;
            }
            if (string.Equals(_broadcastId, frame.BroadcastId, StringComparison.Ordinal)
                && frame.SequenceNumber <= _lastTeacherFrameSequence)
            {
                return;
            }

            if (!string.Equals(_broadcastId, frame.BroadcastId, StringComparison.Ordinal))
            {
                _broadcastId = frame.BroadcastId;
                _lastTeacherFrameSequence = 0;
                _latestTeacherFrame = null;
                _broadcastPaused = false;
            }

            _broadcastActive = true;
            _lastTeacherFrameSequence = frame.SequenceNumber;
            _latestTeacherFrame = frame.JpegData;

            if (_attentionActive) return;
            CloseChatWindow();
            ShowBroadcastWindow();
            _broadcast?.UpdateFrame(frame.JpegData);
            _broadcast?.SetPaused(_broadcastPaused);
            UpdateHookState();
        });

    private void OnChatReceived(object? sender, ChatMessage message) =>
        Dispatcher.Invoke(() =>
        {
            _chatHistory.Add(message);
            while (_chatHistory.Count > 200)
            {
                _chatHistory.RemoveAt(0);
            }

            _hasUnreadChat = true;
            if (_attentionActive || _broadcastActive) return;

            if (_chat is null)
            {
                ShowChatWindow();
            }
            else
            {
                _chat.Append(message);
                _hasUnreadChat = false;
                _ = _chat.Activate();
            }
        });

    private void ShowOverlay(string message)
    {
        _overlay ??= new OverlayWindow();
        _overlay.SetMessage(message);
        if (!_overlay.IsVisible) _overlay.Show();
        _ = _overlay.Activate();
        _overlay.Topmost = true;
    }

    private void ShowBroadcastWindow()
    {
        _broadcast ??= new BroadcastWindow();
        if (_latestTeacherFrame is not null)
        {
            _broadcast.UpdateFrame(_latestTeacherFrame);
        }

        if (!_broadcast.IsVisible) _broadcast.Show();
        _ = _broadcast.Activate();
        _broadcast.Topmost = true;
        _broadcast.SetPaused(_broadcastPaused);
        _broadcast.ReassertFullscreen();
    }

    private void CloseOverlayWindow()
    {
        if (_overlay is null) return;
        _overlay.Close();
        _overlay = null;
    }

    private void CloseBroadcastWindow()
    {
        if (_broadcast is null) return;
        _broadcast.Close();
        _broadcast = null;
    }

    private void ShowChatWindow()
    {
        if (_chat is null)
        {
            _chat = new ChatWindow();
            foreach (var message in _chatHistory)
            {
                _chat.Append(message);
            }

            _chat.MessageSubmitted += OnChatMessageSubmitted;
            _chat.Closed += (_, _) => _chat = null;
        }

        if (!_chat.IsVisible) _chat.Show();
        _chat.Topmost = true;
        _ = _chat.Activate();
        _hasUnreadChat = false;
    }

    private void OnChatMessageSubmitted(object? sender, string body)
    {
        if (_hub is null) return;

        var local = new ChatMessage(
            Guid.NewGuid().ToString("N"),
            ChatDirection.StudentToTeacher,
            Environment.MachineName,
            null,
            body,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        _chatHistory.Add(local);
        while (_chatHistory.Count > 200)
        {
            _chatHistory.RemoveAt(0);
        }

        _chat?.Append(local);
        _ = _hub.SendChatToTeacherAsync(body);
    }

    private void CloseChatWindow()
    {
        if (_chat is null) return;
        _chat.Close();
        _chat = null;
    }

    private void OnRecoveryTriggered(
        object? sender,
        AttentionRecoveryDecision decision) =>
        Dispatcher.Invoke(() =>
        {
            ReleaseManagedUi();
            if (decision.CommandId is not null)
            {
                _ = _hub?.ReportCommandResultAsync(
                    decision.CommandId,
                    RemoteCommandKind.Attention,
                    CommandExecutionState.Applied,
                    $"Auto-unlock recovery: {decision.Description}");
            }
        });
    private void OnRemoteSessionStatusChanged(
        object? sender,
        RemoteSessionStatus status) =>
        Dispatcher.Invoke(() =>
        {
            if (status.State == RemoteSessionState.Active
                && _remoteSessions?.Current is { } session)
            {
                if (_remoteBanner is null)
                {
                    _remoteBanner = new RemoteSessionBanner();
                    _remoteBanner.ReleaseRequested += OnRemoteBannerRelease;
                }

                _remoteBanner.SetSession(session);
                if (!_remoteBanner.IsVisible) _remoteBanner.Show();
                _remoteBanner.Topmost = true;
                return;
            }

            CloseRemoteBanner();
        });

    private void OnRemoteBannerRelease(
        object? sender,
        EventArgs eventArgs) =>
        ReleaseRemoteSession("Dilepas siswa dari banner");

    private void OnRemoteEmergencyRelease(
        object? sender,
        EventArgs eventArgs) =>
        Dispatcher.Invoke(() =>
            ReleaseRemoteSession("Emergency release Ctrl+Alt+Q"));

    private void ReleaseRemoteSession(string reason)
    {
        _remoteSessions?.EndLocal(
            RemoteSessionState.EmergencyReleased,
            reason);
        CloseRemoteBanner();
    }

    private void CloseRemoteBanner()
    {
        if (_remoteBanner is null) return;
        _remoteBanner.ReleaseRequested -= OnRemoteBannerRelease;
        _remoteBanner.Close();
        _remoteBanner = null;
    }


    private void ReleaseManagedUi()
    {
        _attentionActive = false;
        _broadcastActive = false;
        _broadcastPaused = false;
        _broadcastId = null;
        _lastTeacherFrameSequence = 0;
        _latestTeacherFrame = null;
        _recoveryState?.SetAttention(
            active: false,
            commandId: null,
            DateTimeOffset.UtcNow);
        CloseOverlayWindow();
        CloseBroadcastWindow();
        UpdateHookState();
    }

    private static bool IsEmergencyUnlockActive() =>
        EmergencyUnlockStore.TryGetActive(
            DateTimeOffset.UtcNow,
            out _);

    private void UpdateHookState()
    {
        if (_attentionActive || _broadcastActive)
        {
            _hook?.Enable();
            return;
        }

        _hook?.Disable();
        if (_hasUnreadChat) ShowChatWindow();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        _attentionActive = false;
        _broadcastActive = false;
        _hook?.Dispose();
        _keyboardActivity?.Dispose();
        CloseOverlayWindow();
        CloseBroadcastWindow();
        CloseChatWindow();
        if (_lesson is not null)
        {
            _lesson.Close();
            _lesson = null;
        }
        if (_hub is not null) _hub.LessonReceived -= OnLessonReceived;
        CloseRemoteBanner();
        if (_remoteSessions is not null)
        {
            _remoteSessions.StatusChanged -= OnRemoteSessionStatusChanged;
        }
        if (_remoteHotkey is not null)
        {
            _remoteHotkey.Pressed -= OnRemoteEmergencyRelease;
        }

        if (_host is not null)
        {
            await _host.StopAsync(TimeSpan.FromSeconds(2));
            _host.Dispose();
        }

        base.OnExit(e);
    }
}
