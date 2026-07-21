using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace LabKom.Student.Desktop.Services;

/// <summary>
/// Menghitung jumlah key-down global tanpa menyimpan virtual-key, scan-code,
/// karakter, urutan tombol, atau isi field. Counter di-drain oleh telemetry.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class KeyboardActivityMeter : IDisposable
{
    private const int WhKeyboardLowLevel = 13;
    private const int WmKeyDown = 0x0100;
    private const int WmSystemKeyDown = 0x0104;

    private delegate IntPtr LowLevelKeyboardProc(
        int code,
        IntPtr message,
        IntPtr data);

    private readonly LowLevelKeyboardProc _callback;
    private IntPtr _hook;
    private long _keyDownCount;

    public KeyboardActivityMeter()
    {
        _callback = HookCallback;
    }

    public bool Start()
    {
        if (_hook != IntPtr.Zero) return true;
        _hook = SetWindowsHookEx(
            WhKeyboardLowLevel,
            _callback,
            GetModuleHandle(null),
            0);
        if (_hook == IntPtr.Zero)
        {
            Trace.TraceWarning(
                "Telemetry aktivitas keyboard tidak tersedia. Win32Error={0}",
                Marshal.GetLastWin32Error());
        }

        return _hook != IntPtr.Zero;
    }

    public int DrainCount()
    {
        var value = Interlocked.Exchange(ref _keyDownCount, 0);
        return (int)Math.Clamp(value, 0, 20_000);
    }

    private IntPtr HookCallback(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0
            && message.ToInt64() is WmKeyDown or WmSystemKeyDown)
        {
            Interlocked.Increment(ref _keyDownCount);
        }

        return CallNextHookEx(_hook, code, message, data);
    }

    public void Dispose()
    {
        if (_hook == IntPtr.Zero) return;
        _ = UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(
        int hookId,
        LowLevelKeyboardProc callback,
        IntPtr module,
        uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(
        IntPtr hook,
        int code,
        IntPtr message,
        IntPtr data);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? moduleName);
}
