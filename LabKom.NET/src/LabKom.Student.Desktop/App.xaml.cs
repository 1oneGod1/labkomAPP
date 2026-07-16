using System.Windows;
using LabKom.Shared.Contracts;
using LabKom.Shared.Devices;
using LabKom.Shared.Discovery;
using LabKom.Shared.Hub;
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
    private OverlayWindow? _overlay;
    private BroadcastWindow? _broadcast;
    private ChatWindow? _chat;
    private KeyboardHook? _hook;
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

        var sharedSecret = Environment.GetEnvironmentVariable("LABKOM_SHARED_SECRET")
                           ?? builder.Configuration["Desktop:SharedSecret"];
        if (!HubSecurity.IsStrongSecret(sharedSecret))
        {
            MessageBox.Show(
                $"LABKOM_SHARED_SECRET wajib diisi minimal {HubSecurity.MinimumSecretLength} karakter.",
                "Konfigurasi LabKom Student belum lengkap",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(-1);
            return;
        }

        builder.Services.AddSingleton<MachineIdentity>();
        builder.Services.AddSingleton<TeacherEndpointStore>();
        builder.Services.AddSingleton<CaptureProfileState>();
        builder.Services.AddSingleton<IScreenCaptureSource, GdiScreenCapture>();
        builder.Services.AddSingleton<ActivityMonitor>();
        builder.Services.AddSingleton<FileDownloader>();
        builder.Services.AddSingleton<DesktopHubClient>();
        builder.Services.AddHostedService<DiscoveryListener>();
        builder.Services.AddHostedService<DesktopConnectionWorker>();
        builder.Services.AddHostedService<ScreenStreamWorker>();
        builder.Services.AddHostedService<ActivityWorker>();

        _host = builder.Build();

        _hub = _host.Services.GetRequiredService<DesktopHubClient>();
        _hub.AttentionReceived += OnAttentionReceived;
        _hub.BroadcastSignalReceived += OnBroadcastSignalReceived;
        _hub.TeacherFrameReceived += OnTeacherFrameReceived;
        _hub.ChatReceived += OnChatReceived;

        _hook = new KeyboardHook();
        await _host.StartAsync();
    }

    private void OnAttentionReceived(object? sender, AttentionCommand command) =>
        Dispatcher.Invoke(() =>
        {
            _attentionActive = command.Enabled;
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
        CloseOverlayWindow();
        CloseBroadcastWindow();
        CloseChatWindow();

        if (_host is not null)
        {
            await _host.StopAsync(TimeSpan.FromSeconds(2));
            _host.Dispose();
        }

        base.OnExit(e);
    }
}
