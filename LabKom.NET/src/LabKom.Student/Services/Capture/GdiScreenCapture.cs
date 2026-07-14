using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using LabKom.Shared.Contracts;
using Microsoft.Extensions.Logging;

namespace LabKom.Student.Services.Capture;

/// <summary>
/// GDI BitBlt-based screen capture untuk primary monitor.
/// Sederhana, kompatibel di semua versi Windows. DXGI Desktop Duplication
/// bisa ditambahkan di Phase 3 untuk performa lebih tinggi.
/// </summary>
[SupportedOSPlatform("windows")]
public class GdiScreenCapture : IScreenCaptureSource
{
    private readonly ILogger<GdiScreenCapture> _logger;
    private readonly ImageCodecInfo _jpegCodec;

    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int nIndex);
    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;

    public GdiScreenCapture(ILogger<GdiScreenCapture> logger)
    {
        _logger = logger;
        _jpegCodec = ImageCodecInfo.GetImageEncoders()
            .First(c => c.FormatID == ImageFormat.Jpeg.Guid);
    }

    public ScreenFrame? CaptureFrame(string pcName, CaptureProfile profile, int targetWidth, int targetHeight, int jpegQuality)
    {
        try
        {
            var screenW = GetSystemMetrics(SM_CXSCREEN);
            var screenH = GetSystemMetrics(SM_CYSCREEN);
            if (screenW <= 0 || screenH <= 0)
            {
                _logger.LogDebug("GetSystemMetrics tidak mengembalikan ukuran valid (kemungkinan Session 0)");
                return null;
            }

            using var sourceBmp = new Bitmap(screenW, screenH, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(sourceBmp))
            {
                g.CopyFromScreen(0, 0, 0, 0, new Size(screenW, screenH), CopyPixelOperation.SourceCopy);
            }

            using var resized = ResizeWithAspect(sourceBmp, targetWidth, targetHeight);
            using var ms = new MemoryStream();

            var encoderParams = new EncoderParameters(1);
            encoderParams.Param[0] = new EncoderParameter(Encoder.Quality, (long)Math.Clamp(jpegQuality, 30, 95));
            resized.Save(ms, _jpegCodec, encoderParams);

            return ScreenFrame.Create(pcName, profile, resized.Width, resized.Height, ms.ToArray());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Capture frame gagal");
            return null;
        }
    }

    private static Bitmap ResizeWithAspect(Bitmap src, int maxW, int maxH)
    {
        var ratio = Math.Min((double)maxW / src.Width, (double)maxH / src.Height);
        var w = Math.Max(1, (int)(src.Width * ratio));
        var h = Math.Max(1, (int)(src.Height * ratio));
        var dst = new Bitmap(w, h, PixelFormat.Format24bppRgb);
        using var g = Graphics.FromImage(dst);
        g.InterpolationMode = InterpolationMode.HighQualityBilinear;
        g.DrawImage(src, 0, 0, w, h);
        return dst;
    }
}
