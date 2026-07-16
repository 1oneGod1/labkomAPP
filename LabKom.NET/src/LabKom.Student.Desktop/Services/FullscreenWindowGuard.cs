using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Win32;

namespace LabKom.Student.Desktop.Services;

/// <summary>
/// Menjaga window kelas di atas taskbar dan meliputi seluruh virtual desktop,
/// termasuk monitor dengan koordinat negatif.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class FullscreenWindowGuard : IDisposable
{
    private static readonly IntPtr TopMost = new(-1);

    private const int ExtendedStyle = -20;
    private const int ToolWindowStyle = 0x00000080;
    private const int VirtualLeft = 76;
    private const int VirtualTop = 77;
    private const int VirtualWidth = 78;
    private const int VirtualHeight = 79;
    private const uint ShowWindow = 0x0040;
    private const uint FrameChanged = 0x0020;

    private readonly Window _window;
    private bool _disposed;
    private bool _scheduled;

    public FullscreenWindowGuard(Window window)
    {
        _window = window;
        _window.SourceInitialized += OnSourceInitialized;
        _window.Loaded += OnLoaded;
        _window.Deactivated += OnDeactivated;
        _window.StateChanged += OnStateChanged;
        _window.Closed += OnClosed;
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
    }

    public void Reassert()
    {
        if (_disposed) return;

        var handle = new WindowInteropHelper(_window).Handle;
        if (handle == IntPtr.Zero) return;

        var width = GetSystemMetrics(VirtualWidth);
        var height = GetSystemMetrics(VirtualHeight);
        if (width <= 0 || height <= 0) return;

        _window.Topmost = true;
        if (!SetWindowPos(
                handle,
                TopMost,
                GetSystemMetrics(VirtualLeft),
                GetSystemMetrics(VirtualTop),
                width,
                height,
                ShowWindow | FrameChanged))
        {
            Trace.TraceError(
                "LabKom fullscreen guard gagal. Win32Error={0}",
                Marshal.GetLastWin32Error());
        }
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(_window).Handle;
        var styles = GetWindowLong(handle, ExtendedStyle);
        _ = SetWindowLong(handle, ExtendedStyle, styles | ToolWindowStyle);
        Reassert();
    }

    private void OnLoaded(object sender, RoutedEventArgs e) => Reassert();

    private void OnDeactivated(object? sender, EventArgs e)
    {
        ScheduleReassert(activate: true);
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        if (_window.WindowState != WindowState.Normal)
        {
            _window.WindowState = WindowState.Normal;
        }

        ScheduleReassert(activate: false);
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) =>
        ScheduleReassert(activate: false);

    private void ScheduleReassert(bool activate)
    {
        if (_disposed || _scheduled) return;
        _scheduled = true;

        _window.Dispatcher.BeginInvoke(
            DispatcherPriority.Send,
            new Action(() =>
            {
                _scheduled = false;
                if (_disposed) return;

                Reassert();
                if (activate)
                {
                    _ = _window.Activate();
                    _ = _window.Focus();
                }
            }));
    }

    private void OnClosed(object? sender, EventArgs e) => Dispose();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        _window.SourceInitialized -= OnSourceInitialized;
        _window.Loaded -= OnLoaded;
        _window.Deactivated -= OnDeactivated;
        _window.StateChanged -= OnStateChanged;
        _window.Closed -= OnClosed;
    }

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr window,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr window, int index);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr window, int index, int value);
}
