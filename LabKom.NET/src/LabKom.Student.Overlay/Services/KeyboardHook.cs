using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Windows.Input;

namespace LabKom.Student.Overlay.Services;

/// <summary>
/// Low-level keyboard hook (WH_KEYBOARD_LL) untuk memblokir kombinasi tombol
/// saat overlay aktif: Alt+Tab, Alt+F4, Win, Ctrl+Esc.
/// Ctrl+Shift+Esc (Task Manager) tidak bisa diblokir tanpa kernel-mode driver.
/// </summary>
[SupportedOSPlatform("windows")]
public class KeyboardHook : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
    private readonly LowLevelKeyboardProc _proc;
    private IntPtr _hookId = IntPtr.Zero;
    private bool _enabled;

    public KeyboardHook()
    {
        _proc = HookCallback;
    }

    public void Enable()
    {
        if (_hookId != IntPtr.Zero) return;
        using var process = Process.GetCurrentProcess();
        using var module = process.MainModule!;
        _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(module.ModuleName), 0);
        _enabled = true;
    }

    public void Disable()
    {
        _enabled = false;
        if (_hookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && _enabled && (wParam == WM_KEYDOWN || wParam == WM_SYSKEYDOWN))
        {
            int vkCode = Marshal.ReadInt32(lParam);
            var key = KeyInterop.KeyFromVirtualKey(vkCode);

            bool altDown = (GetKeyState((int)Key.LeftAlt) & 0x8000) != 0
                           || (GetKeyState((int)Key.RightAlt) & 0x8000) != 0;
            bool ctrlDown = (GetKeyState((int)Key.LeftCtrl) & 0x8000) != 0
                            || (GetKeyState((int)Key.RightCtrl) & 0x8000) != 0;

            // Blokir: Win, Alt+Tab, Alt+F4, Alt+Esc, Ctrl+Esc, F11
            if (key is Key.LWin or Key.RWin) return (IntPtr)1;
            if (altDown && key is Key.Tab or Key.F4 or Key.Escape) return (IntPtr)1;
            if (ctrlDown && key is Key.Escape) return (IntPtr)1;
            if (key == Key.F11) return (IntPtr)1;
        }
        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    public void Dispose() => Disable();

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int nVirtKey);
}
