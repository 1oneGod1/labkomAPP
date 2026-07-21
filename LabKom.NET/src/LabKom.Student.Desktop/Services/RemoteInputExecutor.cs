using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using LabKom.Shared.Contracts;
using LabKom.Student.Desktop.Services.Capture;
using Microsoft.Extensions.Logging;

namespace LabKom.Student.Desktop.Services;

[SupportedOSPlatform("windows")]
public sealed class RemoteInputExecutor : IDisposable
{
    private const uint InputMouse = 0;
    private const uint InputKeyboard = 1;
    private const uint MouseMove = 0x0001;
    private const uint MouseLeftDown = 0x0002;
    private const uint MouseLeftUp = 0x0004;
    private const uint MouseRightDown = 0x0008;
    private const uint MouseRightUp = 0x0010;
    private const uint MouseMiddleDown = 0x0020;
    private const uint MouseMiddleUp = 0x0040;
    private const uint MouseWheel = 0x0800;
    private const uint MouseVirtualDesk = 0x4000;
    private const uint MouseAbsolute = 0x8000;
    private const uint KeyUp = 0x0002;
    private const int VkControl = 0x11;
    private const int VkMenu = 0x12;
    private const int VkDelete = 0x2E;
    private const int VkQ = 0x51;
    private const int VkLeftWindows = 0x5B;
    private const int VkRightWindows = 0x5C;
    private const int SmXVirtualScreen = 76;
    private const int SmYVirtualScreen = 77;
    private const int SmCxVirtualScreen = 78;
    private const int SmCyVirtualScreen = 79;

    private readonly object _gate = new();
    private readonly IScreenCaptureSource _capture;
    private readonly RemoteSessionController _sessions;
    private readonly ILogger<RemoteInputExecutor> _logger;
    private readonly HashSet<int> _pressedKeys = new();

    public RemoteInputExecutor(
        IScreenCaptureSource capture,
        RemoteSessionController sessions,
        ILogger<RemoteInputExecutor> logger)
    {
        _capture = capture;
        _sessions = sessions;
        _logger = logger;
        _sessions.StatusChanged += OnSessionStatusChanged;
    }

    public bool TryExecute(AcceptedRemoteInput accepted)
    {
        lock (_gate)
        {
            try
            {
                return accepted.Input.Kind switch
                {
                    RemoteInputKind.MouseMove
                        or RemoteInputKind.MouseButtonDown
                        or RemoteInputKind.MouseButtonUp
                        or RemoteInputKind.MouseWheel =>
                        ExecuteMouse(accepted),
                    RemoteInputKind.KeyDown or RemoteInputKind.KeyUp =>
                        ExecuteKeyboard(accepted.Input),
                    _ => false,
                };
            }
            catch (Exception exception)
            {
                _logger.LogWarning(
                    exception,
                    "Input remote {Kind} gagal diterapkan",
                    accepted.Input.Kind);
                return false;
            }
        }
    }

    private bool ExecuteMouse(AcceptedRemoteInput accepted)
    {
        var monitors = _capture.GetMonitors();
        var monitor = monitors.FirstOrDefault(candidate =>
                          string.Equals(
                              candidate.Id,
                              accepted.Session.MonitorId,
                              StringComparison.OrdinalIgnoreCase))
                      ?? monitors.FirstOrDefault(candidate => candidate.IsPrimary)
                      ?? monitors.FirstOrDefault();
        if (monitor is null) return false;

        var screenX = monitor.Left
                      + (int)Math.Round(
                          accepted.Input.NormalizedX / 65_535d
                          * Math.Max(0, monitor.Width - 1));
        var screenY = monitor.Top
                      + (int)Math.Round(
                          accepted.Input.NormalizedY / 65_535d
                          * Math.Max(0, monitor.Height - 1));
        var virtualLeft = GetSystemMetrics(SmXVirtualScreen);
        var virtualTop = GetSystemMetrics(SmYVirtualScreen);
        var virtualWidth = Math.Max(1, GetSystemMetrics(SmCxVirtualScreen));
        var virtualHeight = Math.Max(1, GetSystemMetrics(SmCyVirtualScreen));
        var absoluteX = Math.Clamp(
            (int)Math.Round(
                (screenX - virtualLeft)
                * 65_535d
                / Math.Max(1, virtualWidth - 1)),
            0,
            65_535);
        var absoluteY = Math.Clamp(
            (int)Math.Round(
                (screenY - virtualTop)
                * 65_535d
                / Math.Max(1, virtualHeight - 1)),
            0,
            65_535);

        var flags = MouseMove | MouseAbsolute | MouseVirtualDesk;
        uint mouseData = 0;
        flags |= accepted.Input.Kind switch
        {
            RemoteInputKind.MouseButtonDown =>
                ButtonFlag(accepted.Input.Button, down: true),
            RemoteInputKind.MouseButtonUp =>
                ButtonFlag(accepted.Input.Button, down: false),
            RemoteInputKind.MouseWheel => MouseWheel,
            _ => 0,
        };
        if (accepted.Input.Kind == RemoteInputKind.MouseWheel)
        {
            mouseData = unchecked((uint)accepted.Input.WheelDelta);
        }

        var input = new NativeInput
        {
            Type = InputMouse,
            Data = new InputUnion
            {
                Mouse = new MouseInput
                {
                    X = absoluteX,
                    Y = absoluteY,
                    MouseData = mouseData,
                    Flags = flags,
                },
            },
        };
        return SendInput(1, [input], Marshal.SizeOf<NativeInput>()) == 1;
    }

    private bool ExecuteKeyboard(RemoteInputCommand input)
    {
        var virtualKey = input.VirtualKey;
        if (virtualKey is VkLeftWindows or VkRightWindows) return false;

        var isDown = input.Kind == RemoteInputKind.KeyDown;
        if (isDown
            && ((virtualKey == VkDelete
                 && _pressedKeys.Contains(VkControl)
                 && _pressedKeys.Contains(VkMenu))
                || (virtualKey == VkQ
                    && _pressedKeys.Contains(VkControl)
                    && _pressedKeys.Contains(VkMenu))))
        {
            return false;
        }

        if (isDown)
        {
            _pressedKeys.Add(virtualKey);
        }
        else
        {
            _pressedKeys.Remove(virtualKey);
        }

        var native = new NativeInput
        {
            Type = InputKeyboard,
            Data = new InputUnion
            {
                Keyboard = new KeyboardInput
                {
                    VirtualKey = checked((ushort)virtualKey),
                    Flags = isDown ? 0u : KeyUp,
                },
            },
        };
        return SendInput(1, [native], Marshal.SizeOf<NativeInput>()) == 1;
    }

    public void ReleaseAll()
    {
        lock (_gate)
        {
            foreach (var virtualKey in _pressedKeys.ToArray())
            {
                var input = new NativeInput
                {
                    Type = InputKeyboard,
                    Data = new InputUnion
                    {
                        Keyboard = new KeyboardInput
                        {
                            VirtualKey = checked((ushort)virtualKey),
                            Flags = KeyUp,
                        },
                    },
                };
                _ = SendInput(
                    1,
                    [input],
                    Marshal.SizeOf<NativeInput>());
            }

            _pressedKeys.Clear();
        }
    }

    private void OnSessionStatusChanged(
        object? sender,
        RemoteSessionStatus status)
    {
        if (status.State != RemoteSessionState.Active) ReleaseAll();
    }

    private static uint ButtonFlag(
        RemoteMouseButton button,
        bool down) =>
        (button, down) switch
        {
            (RemoteMouseButton.Left, true) => MouseLeftDown,
            (RemoteMouseButton.Left, false) => MouseLeftUp,
            (RemoteMouseButton.Right, true) => MouseRightDown,
            (RemoteMouseButton.Right, false) => MouseRightUp,
            (RemoteMouseButton.Middle, true) => MouseMiddleDown,
            (RemoteMouseButton.Middle, false) => MouseMiddleUp,
            _ => 0,
        };

    public void Dispose()
    {
        _sessions.StatusChanged -= OnSessionStatusChanged;
        ReleaseAll();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeInput
    {
        public uint Type;
        public InputUnion Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MouseInput Mouse;
        [FieldOffset(0)] public KeyboardInput Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(
        uint inputCount,
        [In] NativeInput[] inputs,
        int inputSize);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);
}
