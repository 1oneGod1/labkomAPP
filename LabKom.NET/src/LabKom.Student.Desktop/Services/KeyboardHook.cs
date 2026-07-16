using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace LabKom.Student.Desktop.Services;

/// <summary>
/// Low-level keyboard hook untuk mode Attention/Broadcast. Hook berjalan hanya
/// pada sesi desktop siswa dan tidak mencoba mencegat secure attention sequence
/// Ctrl+Alt+Delete, yang memang dikendalikan Windows.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class KeyboardHook : IDisposable
{
    private const int WhKeyboardLowLevel = 13;
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int WmSystemKeyDown = 0x0104;
    private const int WmSystemKeyUp = 0x0105;

    private const uint LlkhfAltDown = 0x20;
    private const int VkControl = 0x11;
    private const int VkMenu = 0x12;

    private delegate IntPtr LowLevelKeyboardProc(int code, IntPtr message, IntPtr data);

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardHookData
    {
        public uint VirtualKey;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    private readonly LowLevelKeyboardProc _callback;
    private IntPtr _hook = IntPtr.Zero;
    private bool _enabled;

    public KeyboardHook()
    {
        _callback = HookCallback;
    }

    public bool IsEnabled => _enabled && _hook != IntPtr.Zero;

    public bool Enable()
    {
        if (IsEnabled) return true;

        _hook = SetWindowsHookEx(
            WhKeyboardLowLevel,
            _callback,
            GetModuleHandle(null),
            0);
        _enabled = _hook != IntPtr.Zero;
        if (!_enabled)
        {
            Trace.TraceError(
                "LabKom keyboard hook gagal dipasang. Win32Error={0}",
                Marshal.GetLastWin32Error());
        }

        return _enabled;
    }

    public void Disable()
    {
        _enabled = false;
        if (_hook == IntPtr.Zero) return;

        _ = UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }

    private IntPtr HookCallback(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0
            && _enabled
            && IsKeyboardMessage(message)
            && ShouldBlock(Marshal.PtrToStructure<KeyboardHookData>(data)))
        {
            return new IntPtr(1);
        }

        return CallNextHookEx(_hook, code, message, data);
    }

    private static bool IsKeyboardMessage(IntPtr message)
    {
        var value = message.ToInt64();
        return value is WmKeyDown or WmKeyUp or WmSystemKeyDown or WmSystemKeyUp;
    }

    private static bool ShouldBlock(KeyboardHookData key)
    {
        var altDown = (key.Flags & LlkhfAltDown) != 0 || IsPressed(VkMenu);
        var controlDown = IsPressed(VkControl);
        return KeyboardBlockPolicy.ShouldBlock((int)key.VirtualKey, altDown, controlDown);
    }

    private static bool IsPressed(int virtualKey) => (GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    public void Dispose() => Disable();

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

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? moduleName);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);
}
