using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LabKom.Student.Desktop.Services;

namespace LabKom.Student.Desktop;

public partial class BroadcastWindow : Window
{
    private readonly FullscreenWindowGuard _fullscreenGuard;

    public BroadcastWindow()
    {
        InitializeComponent();
        _fullscreenGuard = new FullscreenWindowGuard(this);
        PreviewKeyDown += BlockKey;
        PreviewKeyUp += BlockKey;
    }

    public bool UpdateFrame(byte[] jpegData)
    {
        if (jpegData.Length == 0) return false;

        try
        {
            using var stream = new MemoryStream(jpegData, writable: false);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();

            FrameImage.Source = bitmap;
            return true;
        }
        catch
        {
            // Frame rusak diabaikan; gambar sebelumnya tetap tampil.
            return false;
        }
    }

    public void SetPaused(bool paused)
    {
        StatusText.Text = paused
            ? "DIJEDA - Layar Instruktur"
            : "LIVE - Layar Instruktur";
        StatusDot.Fill = paused
            ? Brushes.Gold
            : new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44));
    }

    private static void BlockKey(object sender, KeyEventArgs e)
    {
        e.Handled = true;
    }

    public void ReassertFullscreen() => _fullscreenGuard.Reassert();
}
