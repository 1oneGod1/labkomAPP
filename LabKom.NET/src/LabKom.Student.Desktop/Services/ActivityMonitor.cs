using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using LabKom.Shared.Contracts;

namespace LabKom.Student.Desktop.Services;

/// <summary>
/// Polling-based active window monitor. Tiap tick: ambil HWND foreground,
/// title, dan nama process. Tidak persist state — caller bertanggung jawab
/// mendeteksi perubahan.
/// </summary>
[SupportedOSPlatform("windows")]
public class ActivityMonitor
{
    private static readonly HashSet<string> BrowserProcesses =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "chrome", "msedge", "firefox", "brave", "opera",
            "opera_gx", "vivaldi", "iexplore",
        };

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint Size;
        public uint Time;
    }

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetLastInputInfo(ref LastInputInfo info);

    public ActivitySnapshot? Snapshot()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return null;

        var sb = new StringBuilder(512);
        GetWindowText(hwnd, sb, sb.Capacity);
        var title = sb.ToString();
        if (string.IsNullOrWhiteSpace(title)) title = "(tanpa judul)";

        GetWindowThreadProcessId(hwnd, out var pid);
        string? procName = null;
        try
        {
            using var p = Process.GetProcessById((int)pid);
            procName = p.ProcessName;
        }
        catch { /* process mungkin sudah exit */ }

        return new ActivitySnapshot(
            title,
            procName,
            Classify(procName),
            GetIdleMilliseconds());
    }

    private static ActivityCategory Classify(string? processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
            return ActivityCategory.System;
        if (BrowserProcesses.Contains(processName))
            return ActivityCategory.WebBrowser;
        if (processName.Equals("explorer", StringComparison.OrdinalIgnoreCase)
            || processName.Equals("dwm", StringComparison.OrdinalIgnoreCase))
            return ActivityCategory.Desktop;
        return ActivityCategory.Application;
    }

    private static long GetIdleMilliseconds()
    {
        var info = new LastInputInfo { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
        return GetLastInputInfo(ref info)
            ? unchecked((uint)Environment.TickCount - info.Time)
            : 0;
    }
}

public sealed record ActivitySnapshot(
    string WindowTitle,
    string? ProcessName,
    ActivityCategory Category,
    long IdleMilliseconds);
