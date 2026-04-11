# blockAltTab.ps1
# Script ini menginstall Low-Level Keyboard Hook untuk memblokir Alt+Tab, Alt+Esc, Win+Tab, dll.
# Dijalankan sebagai proses terpisah saat mode kiosk/lock aktif.
# Untuk berhenti: kirim sinyal lewat file flag atau kill proses ini.

param(
    [string]$FlagFile = "",
    [int]$ElectronPID = 0
)

Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Threading;
using System.IO;
using System.Diagnostics;

public class KeyboardBlocker {
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN     = 0x0100;
    private const int WM_SYSKEYDOWN  = 0x0104;

    // Virtual key codes
    private const int VK_TAB    = 0x09;
    private const int VK_ESCAPE = 0x1B;
    private const int VK_F4     = 0x73;
    private const int VK_F11    = 0x7A;
    private const int VK_LWIN   = 0x5B;
    private const int VK_RWIN   = 0x5C;
    private const int VK_D      = 0x44;
    private const int VK_E      = 0x45;
    private const int VK_R      = 0x52;
    private const int VK_L      = 0x4C;
    private const int VK_M      = 0x4D;
    private const int VK_X      = 0x58;
    private const int VK_P      = 0x50;  // Win+P (Projection)
    private const int VK_I      = 0x49;  // Win+I (Settings)
    private const int VK_S      = 0x53;  // Win+S (Search)
    private const int VK_A      = 0x41;  // Win+A (Action Center)
    private const int VK_SPACE  = 0x20;  // Space
    private const int VK_N      = 0x4E;  // Win+N (Notifications)

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT {
        public uint   vkCode;
        public uint   scanCode;
        public uint   flags;
        public uint   time;
        public IntPtr dwExtraInfo;
    }

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PeekMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref MSG lpMsg);

    // ── Tambahan: SetForegroundWindow untuk paksa fokus kembali ──
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG {
        public IntPtr hwnd;
        public uint   message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint   time;
        public int    pt_x;
        public int    pt_y;
    }

    private static IntPtr _hookID = IntPtr.Zero;
    private static LowLevelKeyboardProc _proc;
    private static GCHandle _procHandle;
    public static string FlagFilePath = "";
    public static int ElectronPID = 0;
    public static volatile bool IsRunning = true;
    private static int _flagCheckCounter = 0;
    private static int _focusCheckCounter = 0;

    private static bool IsKeyDown(int vk) {
        return (GetAsyncKeyState(vk) & 0x8000) != 0;
    }

    private static bool IsAltDown() {
        return IsKeyDown(0xA4) || IsKeyDown(0xA5) || IsKeyDown(0x12);
    }

    private static bool IsWinDown() {
        return IsKeyDown(VK_LWIN) || IsKeyDown(VK_RWIN);
    }

    private static bool IsCtrlDown() {
        return IsKeyDown(0xA2) || IsKeyDown(0xA3);
    }

    private static bool IsShiftDown() {
        return IsKeyDown(0xA0) || IsKeyDown(0xA1);
    }

    // Paksa window Electron ke depan
    private static void ForceFocusElectron() {
        if (ElectronPID <= 0) return;
        try {
            var proc = Process.GetProcessById(ElectronPID);
            if (proc == null || proc.HasExited) return;
            IntPtr hwnd = proc.MainWindowHandle;
            if (hwnd == IntPtr.Zero) return;

            IntPtr fgWnd = GetForegroundWindow();
            if (fgWnd == hwnd) return; // Sudah fokus

            // Trick: attach ke thread foreground, set foreground, detach
            uint fgThread = GetWindowThreadProcessId(fgWnd, out _);
            uint ourThread = GetCurrentThreadId();
            if (fgThread != ourThread) {
                AttachThreadInput(ourThread, fgThread, true);
            }

            ShowWindow(hwnd, 9); // SW_RESTORE
            BringWindowToTop(hwnd);
            SetForegroundWindow(hwnd);

            if (fgThread != ourThread) {
                AttachThreadInput(ourThread, fgThread, false);
            }
        } catch {}
    }

    private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam) {
        if (!IsRunning) return CallNextHookEx(_hookID, nCode, wParam, lParam);

        if (nCode >= 0 && (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN)) {
            var kb = (KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(KBDLLHOOKSTRUCT));
            int vk = (int)kb.vkCode;
            bool altDown  = IsAltDown();
            bool winDown  = IsWinDown();
            bool ctrlDown = IsCtrlDown();

            // ── Blokir Alt combinations ──
            if (altDown && vk == VK_TAB) {
                ForceFocusElectron();
                return (IntPtr)1;  // Alt+Tab
            }
            if (altDown && vk == VK_ESCAPE) {
                ForceFocusElectron();
                return (IntPtr)1;  // Alt+Esc
            }
            if (altDown && vk == VK_F4)      return (IntPtr)1;  // Alt+F4
            if (altDown && vk == VK_SPACE)    return (IntPtr)1;  // Alt+Space (window menu)

            // ── Blokir Win combinations ──
            if (winDown && vk == VK_D)       return (IntPtr)1;  // Win+D
            if (winDown && vk == VK_E)       return (IntPtr)1;  // Win+E
            if (winDown && vk == VK_R)       return (IntPtr)1;  // Win+R
            if (winDown && vk == VK_L)       return (IntPtr)1;  // Win+L
            if (winDown && vk == VK_M)       return (IntPtr)1;  // Win+M
            if (winDown && vk == VK_X)       return (IntPtr)1;  // Win+X
            if (winDown && vk == VK_TAB)     return (IntPtr)1;  // Win+Tab
            if (winDown && vk == VK_P)       return (IntPtr)1;  // Win+P
            if (winDown && vk == VK_I)       return (IntPtr)1;  // Win+I
            if (winDown && vk == VK_S)       return (IntPtr)1;  // Win+S
            if (winDown && vk == VK_A)       return (IntPtr)1;  // Win+A
            if (winDown && vk == VK_N)       return (IntPtr)1;  // Win+N
            if (winDown && vk == VK_SPACE)   return (IntPtr)1;  // Win+Space (keyboard layout)

            // ── Blokir tombol Win sendirian ──
            if (vk == VK_LWIN || vk == VK_RWIN) return (IntPtr)1;

            // ── Blokir Ctrl combinations ──
            if (ctrlDown && vk == VK_ESCAPE) return (IntPtr)1;  // Ctrl+Esc (Start menu)
            if (ctrlDown && IsShiftDown() && vk == VK_ESCAPE) return (IntPtr)1;  // Ctrl+Shift+Esc (Task Manager)

            // ── Blokir F11 (fullscreen toggle) ──
            if (vk == VK_F11) return (IntPtr)1;
        }

        // PENTING: Kembalikan ke chain secepat mungkin agar Windows tidak melepas hook
        return CallNextHookEx(_hookID, nCode, wParam, lParam);
    }

    private static void CheckFlagFile() {
        // Cek flag file hanya sekali tiap ~500ms (setiap 50 iterasi x 10ms)
        _flagCheckCounter++;
        if (_flagCheckCounter < 50) return;
        _flagCheckCounter = 0;

        if (!string.IsNullOrEmpty(FlagFilePath)) {
            try {
                if (File.Exists(FlagFilePath)) {
                    Console.WriteLine("[KBHOOK] Flag file ditemukan, menghentikan hook...");
                    IsRunning = false;
                }
            } catch {}
        }

        // Cek apakah Electron process masih hidup
        if (ElectronPID > 0) {
            try {
                var proc = Process.GetProcessById(ElectronPID);
                if (proc == null || proc.HasExited) {
                    Console.WriteLine("[KBHOOK] Electron process sudah mati, menghentikan hook...");
                    IsRunning = false;
                }
            } catch {
                Console.WriteLine("[KBHOOK] Electron process tidak ditemukan, menghentikan hook...");
                IsRunning = false;
            }
        }
    }

    private static void CheckAndRecoverFocus() {
        // Cek fokus setiap ~200ms (setiap 20 iterasi x 10ms)
        _focusCheckCounter++;
        if (_focusCheckCounter < 20) return;
        _focusCheckCounter = 0;

        ForceFocusElectron();
    }

    public static void Start() {
        Console.WriteLine("[KBHOOK] Memulai keyboard hook...");
        Console.WriteLine("[KBHOOK] Electron PID: " + ElectronPID);

        // Buat delegate dan pin agar tidak di-GC
        _proc = new LowLevelKeyboardProc(HookCallback);
        _procHandle = GCHandle.Alloc(_proc);

        using (var curProcess = System.Diagnostics.Process.GetCurrentProcess())
        using (var curModule = curProcess.MainModule) {
            _hookID = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(curModule.ModuleName), 0);
        }

        if (_hookID == IntPtr.Zero) {
            Console.WriteLine("[KBHOOK] GAGAL memasang hook! Error: " + Marshal.GetLastWin32Error());
            _procHandle.Free();
            return;
        }

        Console.WriteLine("[KBHOOK] Hook terpasang berhasil (handle: " + _hookID + ")");

        // Proper Win32 message pump - WAJIB untuk low-level hooks
        // Menggunakan PeekMessage loop agar bisa cek flag file secara periodik
        MSG msg;
        while (IsRunning) {
            // Proses semua pending messages
            while (PeekMessage(out msg, IntPtr.Zero, 0, 0, 1)) { // PM_REMOVE = 1
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }

            // Cek flag file & electron alive
            CheckFlagFile();

            // Cek dan pulihkan fokus Electron
            CheckAndRecoverFocus();

            // Sleep sangat singkat agar tidak menghambat message pump
            // tapi juga tidak memakan 100% CPU
            Thread.Sleep(10);
        }

        // Cleanup
        if (_hookID != IntPtr.Zero) {
            UnhookWindowsHookEx(_hookID);
            _hookID = IntPtr.Zero;
            Console.WriteLine("[KBHOOK] Hook dilepas.");
        }
        _procHandle.Free();
        Console.WriteLine("[KBHOOK] Proses selesai.");
    }
}
"@ -ReferencedAssemblies "System.Windows.Forms"

Write-Host "[KBHOOK] Script dimulai, FlagFile: $FlagFile, ElectronPID: $ElectronPID"
[KeyboardBlocker]::FlagFilePath = $FlagFile
[KeyboardBlocker]::ElectronPID = $ElectronPID
[KeyboardBlocker]::Start()
Write-Host "[KBHOOK] Script selesai."
