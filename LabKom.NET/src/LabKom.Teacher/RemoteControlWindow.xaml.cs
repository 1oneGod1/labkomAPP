using System.Diagnostics;
using System.IO;
using System.Threading.Channels;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LabKom.Shared.Contracts;
using LabKom.Teacher.Services;

namespace LabKom.Teacher;

public partial class RemoteControlWindow : Window
{
    private readonly PresenceRegistry _registry;
    private readonly RemoteControlService _remote;
    private readonly RemoteCommandService _commands;
    private readonly string _pcName;
    private readonly string? _monitorId;
    private readonly DispatcherTimer _renewTimer;
    private readonly Channel<RemoteInputCommand> _inputQueue;
    private readonly CancellationTokenSource _closeCts = new();
    private readonly Task _inputPump;
    private readonly Stopwatch _mouseThrottle = Stopwatch.StartNew();

    private RemoteSessionCommand? _session;
    private long _inputSequence;
    private bool _closed;

    public RemoteControlWindow(
        PresenceRegistry registry,
        RemoteControlService remote,
        RemoteCommandService commands,
        string pcName,
        string? monitorId)
    {
        _registry = registry;
        _remote = remote;
        _commands = commands;
        _pcName = pcName;
        _monitorId = monitorId;
        InitializeComponent();

        Title = $"Remote - {pcName}";
        PcNameText.Text = pcName;
        _renewTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(45),
        };
        _renewTimer.Tick += RenewTimer_Tick;
        _inputQueue = Channel.CreateUnbounded<RemoteInputCommand>(
            new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false,
            });
        _inputPump = PumpInputAsync(_closeCts.Token);

        Loaded += Window_Loaded;
        Closed += Window_Closed;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _registry.FrameUpdated += Registry_FrameUpdated;
        _remote.StatusReceived += Remote_StatusReceived;
        if (_registry.Get(_pcName)?.LastFrame is { } frame)
        {
            ApplyFrame(frame);
        }

        await StartModeAsync(RemoteSessionMode.ViewOnly);
        _renewTimer.Start();
        _ = RemoteSurface.Focus();
    }

    private async Task StartModeAsync(RemoteSessionMode mode)
    {
        try
        {
            if (_session is not null)
            {
                await _remote.StopAsync(
                    _session,
                    "Mode sesi remote diganti");
            }

            _session = await _remote.StartAsync(
                _pcName,
                mode,
                _monitorId);
            Interlocked.Exchange(ref _inputSequence, 0);
            await _commands.SetCaptureProfileAsync(
                _pcName,
                CaptureProfile.Focus,
                _monitorId);
            ApplyMode();
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Sesi gagal: {exception.Message}";
            _session = null;
            ApplyMode();
        }
    }

    private void ApplyMode()
    {
        var isControl = _session?.Mode == RemoteSessionMode.Control;
        StatusText.Text = _session switch
        {
            null => "Sesi tidak aktif",
            { Mode: RemoteSessionMode.Control } =>
                "REMOTE CONTROL AKTIF - mouse dan keyboard diteruskan",
            _ => "View-only aktif - input tidak diteruskan",
        };
        StatusText.Foreground = isControl
            ? System.Windows.Media.Brushes.Orange
            : System.Windows.Media.Brushes.LightGreen;
        ControlButton.IsEnabled = !isControl;
        ViewOnlyButton.IsEnabled = isControl;
        RemoteSurface.Cursor = isControl
            ? Cursors.Cross
            : Cursors.Arrow;
    }

    private void Registry_FrameUpdated(object? sender, ScreenFrame frame)
    {
        if (!string.Equals(
                frame.PcName,
                _pcName,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Dispatcher.BeginInvoke(() => ApplyFrame(frame));
    }

    private void ApplyFrame(ScreenFrame frame)
    {
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = new MemoryStream(frame.JpegData);
            bitmap.EndInit();
            bitmap.Freeze();
            RemoteImage.Source = bitmap;
            StreamText.Text =
                $"{frame.CaptureBackend} | {frame.Width}x{frame.Height}"
                + $" | {frame.TargetFramesPerSecond} fps Q{frame.JpegQuality}"
                + $" | capture {frame.CaptureDurationMilliseconds} ms"
                + $" / send {frame.PreviousSendDurationMilliseconds} ms";
        }
        catch
        {
            StreamText.Text = "Frame rusak diabaikan.";
        }
    }

    private async void RenewTimer_Tick(
        object? sender,
        EventArgs eventArgs)
    {
        if (_session is null) return;
        try
        {
            _session = await _remote.RenewAsync(_session);
            if (_session is null) ApplyMode();
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Renew gagal: {exception.Message}";
        }
    }

    private void Remote_StatusReceived(
        object? sender,
        RemoteSessionStatus status)
    {
        if (_session is null
            || !string.Equals(
                status.SessionId,
                _session.SessionId,
                StringComparison.Ordinal))
        {
            return;
        }

        Dispatcher.BeginInvoke(() =>
        {
            if (status.State == RemoteSessionState.Active) return;
            _session = null;
            ApplyMode();
            StatusText.Text =
                $"{status.State}: {status.Message ?? "sesi berakhir"}";
        });
    }

    private async void ViewOnly_Click(
        object sender,
        RoutedEventArgs e) =>
        await StartModeAsync(RemoteSessionMode.ViewOnly);

    private async void Control_Click(
        object sender,
        RoutedEventArgs e) =>
        await StartModeAsync(RemoteSessionMode.Control);

    private async void Stop_Click(
        object sender,
        RoutedEventArgs e)
    {
        await StopSessionAsync("Dihentikan operator");
        Close();
    }

    private void Surface_MouseMove(
        object sender,
        MouseEventArgs e)
    {
        if (!CanControl
            || _mouseThrottle.ElapsedMilliseconds < 33
            || !TryNormalize(e.GetPosition(RemoteImage), out var x, out var y))
        {
            return;
        }

        _mouseThrottle.Restart();
        QueueInput(
            RemoteInputKind.MouseMove,
            x,
            y);
    }

    private void Surface_MouseDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (!CanControl
            || !TryNormalize(e.GetPosition(RemoteImage), out var x, out var y)
            || MouseButton(e.ChangedButton) is not { } button)
        {
            return;
        }

        _ = RemoteSurface.Focus();
        QueueInput(
            RemoteInputKind.MouseButtonDown,
            x,
            y,
            button);
        e.Handled = true;
    }

    private void Surface_MouseUp(
        object sender,
        MouseButtonEventArgs e)
    {
        if (!CanControl
            || !TryNormalize(e.GetPosition(RemoteImage), out var x, out var y)
            || MouseButton(e.ChangedButton) is not { } button)
        {
            return;
        }

        QueueInput(
            RemoteInputKind.MouseButtonUp,
            x,
            y,
            button);
        e.Handled = true;
    }

    private void Surface_MouseWheel(
        object sender,
        MouseWheelEventArgs e)
    {
        if (!CanControl
            || !TryNormalize(e.GetPosition(RemoteImage), out var x, out var y))
        {
            return;
        }

        QueueInput(
            RemoteInputKind.MouseWheel,
            x,
            y,
            wheelDelta: Math.Clamp(e.Delta, -1_200, 1_200));
        e.Handled = true;
    }

    private void Window_PreviewKeyDown(
        object sender,
        KeyEventArgs e)
    {
        if (!CanControl || e.IsRepeat) return;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var virtualKey = KeyInterop.VirtualKeyFromKey(key);
        if (virtualKey <= 0) return;
        QueueInput(
            RemoteInputKind.KeyDown,
            virtualKey: virtualKey);
        e.Handled = true;
    }

    private void Window_PreviewKeyUp(
        object sender,
        KeyEventArgs e)
    {
        if (!CanControl) return;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var virtualKey = KeyInterop.VirtualKeyFromKey(key);
        if (virtualKey <= 0) return;
        QueueInput(
            RemoteInputKind.KeyUp,
            virtualKey: virtualKey);
        e.Handled = true;
    }

    private bool TryNormalize(
        Point point,
        out int normalizedX,
        out int normalizedY)
    {
        normalizedX = 0;
        normalizedY = 0;
        if (RemoteImage.Source is not BitmapSource bitmap
            || RemoteImage.ActualWidth <= 0
            || RemoteImage.ActualHeight <= 0)
        {
            return false;
        }

        var scale = Math.Min(
            RemoteImage.ActualWidth / bitmap.PixelWidth,
            RemoteImage.ActualHeight / bitmap.PixelHeight);
        var renderedWidth = bitmap.PixelWidth * scale;
        var renderedHeight = bitmap.PixelHeight * scale;
        var offsetX = (RemoteImage.ActualWidth - renderedWidth) / 2;
        var offsetY = (RemoteImage.ActualHeight - renderedHeight) / 2;
        if (point.X < offsetX
            || point.Y < offsetY
            || point.X > offsetX + renderedWidth
            || point.Y > offsetY + renderedHeight)
        {
            return false;
        }

        normalizedX = Math.Clamp(
            (int)Math.Round(
                (point.X - offsetX) / renderedWidth * 65_535d),
            0,
            65_535);
        normalizedY = Math.Clamp(
            (int)Math.Round(
                (point.Y - offsetY) / renderedHeight * 65_535d),
            0,
            65_535);
        return true;
    }

    private void QueueInput(
        RemoteInputKind kind,
        int x = 0,
        int y = 0,
        RemoteMouseButton button = RemoteMouseButton.None,
        int wheelDelta = 0,
        int virtualKey = 0)
    {
        var session = _session;
        if (session?.Mode != RemoteSessionMode.Control) return;
        var input = RemoteInputCommand.Create(
            session,
            Interlocked.Increment(ref _inputSequence),
            kind,
            x,
            y,
            button,
            wheelDelta,
            virtualKey);
        _inputQueue.Writer.TryWrite(input);
    }

    private async Task PumpInputAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var input in _inputQueue.Reader.ReadAllAsync(
                               cancellationToken))
            {
                var session = _session;
                if (session is null
                    || !string.Equals(
                        session.SessionId,
                        input.SessionId,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                await _remote.SendInputAsync(session, input);
            }
        }
        catch (OperationCanceledException)
        {
            // Window ditutup.
        }
        catch (Exception exception)
        {
            await Dispatcher.BeginInvoke(() =>
                StatusText.Text = $"Input dihentikan: {exception.Message}");
        }
    }

    private async Task StopSessionAsync(string reason)
    {
        var session = _session;
        _session = null;
        ApplyMode();
        if (session is null) return;
        try
        {
            await _remote.StopAsync(session, reason);
            await _commands.SetCaptureProfileAsync(
                _pcName,
                CaptureProfile.Thumbnail);
        }
        catch
        {
            // Penutupan window tetap dilanjutkan.
        }
    }

    private async void Window_Closed(object? sender, EventArgs e)
    {
        if (_closed) return;
        _closed = true;
        _renewTimer.Stop();
        _registry.FrameUpdated -= Registry_FrameUpdated;
        _remote.StatusReceived -= Remote_StatusReceived;
        await StopSessionAsync("Jendela remote ditutup");
        _inputQueue.Writer.TryComplete();
        _closeCts.Cancel();
        try
        {
            await _inputPump;
        }
        catch (OperationCanceledException)
        {
            // Expected.
        }
        _closeCts.Dispose();
    }

    private bool CanControl =>
        _session is
        {
            Active: true,
            Mode: RemoteSessionMode.Control,
        }
        && IsActive;

    private static RemoteMouseButton? MouseButton(
        System.Windows.Input.MouseButton button) =>
        button switch
        {
            System.Windows.Input.MouseButton.Left =>
                RemoteMouseButton.Left,
            System.Windows.Input.MouseButton.Right =>
                RemoteMouseButton.Right,
            System.Windows.Input.MouseButton.Middle =>
                RemoteMouseButton.Middle,
            _ => null,
        };
}
