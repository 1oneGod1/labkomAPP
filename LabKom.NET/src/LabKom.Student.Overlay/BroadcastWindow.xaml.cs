using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace LabKom.Student.Overlay;

public partial class BroadcastWindow : Window
{
    public BroadcastWindow()
    {
        InitializeComponent();
        PreviewKeyDown += (_, e) => e.Handled = true;
        PreviewKeyUp += (_, e) => e.Handled = true;
    }

    public void UpdateFrame(byte[] jpegData)
    {
        if (jpegData.Length == 0) return;
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = new MemoryStream(jpegData);
            bmp.EndInit();
            bmp.Freeze();
            FrameImage.Source = bmp;
        }
        catch
        {
            // Frame corrupt — tunggu frame berikutnya.
        }
    }
}
