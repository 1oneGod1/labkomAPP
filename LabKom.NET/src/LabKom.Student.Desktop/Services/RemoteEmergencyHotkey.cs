using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Windows.Interop;
using Microsoft.Extensions.Logging;

namespace LabKom.Student.Desktop.Services;

/// <summary>Global Ctrl+Alt+Q untuk melepas sesi remote secara lokal.</summary>
[SupportedOSPlatform("windows")]
public sealed class RemoteEmergencyHotkey : IDisposable
{
    private const int HotkeyId = 0x4C4B;
    private const int WmHotkey = 0x0312;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModNoRepeat = 0x4000;
    private const uint VkQ = 0x51;

    private readonly HwndSource _source;
    private readonly ILogger<RemoteEmergencyHotkey> _logger;
    private bool _registered;

    public RemoteEmergencyHotkey(
        ILogger<RemoteEmergencyHotkey> logger)
    {
        _logger = logger;
        _source = new HwndSource(
            new HwndSourceParameters("LabKomRemoteEmergencyHotkey")
            {
                ParentWindow = new IntPtr(-3),
                WindowStyle = 0,
                Width = 0,
                Height = 0,
            });
        _source.AddHook(WindowProc);
        _registered = RegisterHotKey(
            _source.Handle,
            HotkeyId,
            ModAlt | ModControl | ModNoRepeat,
            VkQ);
        if (!_registered)
        {
            _logger.LogWarning(
                "Ctrl+Alt+Q tidak dapat diregistrasikan. Win32Error={Error}",
                Marshal.GetLastWin32Error());
        }
    }

    public event EventHandler? Pressed;

    public bool IsRegistered => _registered;

    private IntPtr WindowProc(
        IntPtr window,
        int message,
        IntPtr wParam,
        IntPtr lParam,
        ref bool handled)
    {
        if (message == WmHotkey
            && wParam.ToInt32() == HotkeyId)
        {
            handled = true;
            Pressed?.Invoke(this, EventArgs.Empty);
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_registered)
        {
            _ = UnregisterHotKey(_source.Handle, HotkeyId);
            _registered = false;
        }

        _source.RemoveHook(WindowProc);
        _source.Dispose();
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(
        IntPtr window,
        int id,
        uint modifiers,
        uint virtualKey);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr window, int id);
}
