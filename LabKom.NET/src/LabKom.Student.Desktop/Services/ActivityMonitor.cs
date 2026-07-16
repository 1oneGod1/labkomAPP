using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace LabKom.Student.Desktop.Services;

/// <summary>
/// Polling-based active window monitor. Tiap tick: ambil HWND foreground,
/// title, dan nama process. Tidak persist state — caller bertanggung jawab
/// mendeteksi perubahan.
/// </summary>
[SupportedOSPlatform("windows")]
public class ActivityMonitor
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

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

        return new ActivitySnapshot(title, procName);
    }
}

public sealed record ActivitySnapshot(string WindowTitle, string? ProcessName);
