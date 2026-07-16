using System.Windows;
using System.Windows.Input;
using LabKom.Student.Desktop.Services;

namespace LabKom.Student.Desktop;

public partial class OverlayWindow : Window
{
    private readonly FullscreenWindowGuard _fullscreenGuard;

    public OverlayWindow()
    {
        InitializeComponent();
        _fullscreenGuard = new FullscreenWindowGuard(this);
        PreviewKeyDown += BlockKey;
        PreviewKeyUp += BlockKey;
    }

    private static void BlockKey(object sender, KeyEventArgs e)
    {
        e.Handled = true;
    }

    public void SetMessage(string message)
    {
        MessageText.Text = string.IsNullOrWhiteSpace(message)
            ? "Mohon perhatian ke instruktur"
            : message;
        _fullscreenGuard.Reassert();
    }
}
