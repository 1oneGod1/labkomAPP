using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace LabKom.Student.Overlay;

public partial class OverlayWindow : Window
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    public OverlayWindow()
    {
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
        PreviewKeyDown += BlockKey;
        PreviewKeyUp += BlockKey;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        // Sembunyikan dari Alt+Tab list (WS_EX_TOOLWINDOW).
        var hwnd = new WindowInteropHelper(this).Handle;
        var ex = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_TOOLWINDOW);
    }

    private void BlockKey(object sender, KeyEventArgs e)
    {
        e.Handled = true;
    }

    public void SetMessage(string message)
    {
        MessageText.Text = string.IsNullOrWhiteSpace(message)
            ? "Mohon perhatian ke instruktur"
            : message;
    }
}
