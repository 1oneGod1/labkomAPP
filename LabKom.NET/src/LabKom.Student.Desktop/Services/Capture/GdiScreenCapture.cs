using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using LabKom.Shared.Contracts;
using Microsoft.Extensions.Logging;

namespace LabKom.Student.Desktop.Services.Capture;

/// <summary>
/// GDI BitBlt capture untuk seluruh monitor Windows. Implementasi ini menjadi
/// fallback kompatibel saat DXGI Desktop Duplication tidak tersedia.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class GdiScreenCapture : IScreenCaptureSource
{
    private const uint MonitorInfoPrimary = 0x00000001;

    private readonly ILogger<GdiScreenCapture> _logger;
    private readonly ImageCodecInfo _jpegCodec;

    private delegate bool MonitorEnumProc(
        IntPtr monitor,
        IntPtr deviceContext,
        ref NativeRect monitorRect,
        IntPtr data);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfoEx
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect WorkArea;
        public uint Flags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string? DeviceName;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(
        IntPtr deviceContext,
        IntPtr clipRect,
        MonitorEnumProc callback,
        IntPtr data);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfoEx info);

    public GdiScreenCapture(ILogger<GdiScreenCapture> logger)
    {
        _logger = logger;
        _jpegCodec = ImageCodecInfo.GetImageEncoders()
            .First(codec => codec.FormatID == ImageFormat.Jpeg.Guid);
    }

    public IReadOnlyList<MonitorDescriptor> GetMonitors()
    {
        try
        {
            var monitors = new List<MonitorDescriptor>();
            MonitorEnumProc callback = (
                IntPtr monitor,
                IntPtr deviceContext,
                ref NativeRect monitorRect,
                IntPtr data) =>
            {
                var info = new MonitorInfoEx { Size = Marshal.SizeOf<MonitorInfoEx>() };
                if (!GetMonitorInfo(monitor, ref info)) return true;

                var width = info.Monitor.Right - info.Monitor.Left;
                var height = info.Monitor.Bottom - info.Monitor.Top;
                if (width <= 0 || height <= 0) return true;

                var deviceName = string.IsNullOrWhiteSpace(info.DeviceName)
                    ? $"MONITOR-{monitor.ToInt64():X}"
                    : info.DeviceName.Trim();

                monitors.Add(new MonitorDescriptor(
                    deviceName,
                    deviceName,
                    info.Monitor.Left,
                    info.Monitor.Top,
                    width,
                    height,
                    (info.Flags & MonitorInfoPrimary) != 0));
                return true;
            };

            _ = EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero);
            return monitors
                .OrderByDescending(monitor => monitor.IsPrimary)
                .ThenBy(monitor => monitor.Left)
                .ThenBy(monitor => monitor.Top)
                .Take(ContractValidation.MaximumMonitorCount)
                .ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Inventaris monitor gagal");
            return Array.Empty<MonitorDescriptor>();
        }
    }

    public ScreenFrame? CaptureFrame(
        string pcName,
        CaptureProfile profile,
        string? monitorId,
        int targetWidth,
        int targetHeight,
        int jpegQuality,
        long sequenceNumber)
    {
        try
        {
            var monitors = GetMonitors();
            var monitor = monitors.FirstOrDefault(candidate =>
                              string.Equals(candidate.Id, monitorId, StringComparison.OrdinalIgnoreCase))
                          ?? monitors.FirstOrDefault(candidate => candidate.IsPrimary)
                          ?? monitors.FirstOrDefault();
            if (monitor is null)
            {
                _logger.LogDebug("Tidak ada monitor interaktif yang dapat ditangkap");
                return null;
            }

            targetWidth = Math.Clamp(targetWidth, 1, ContractValidation.MaximumFrameDimension);
            targetHeight = Math.Clamp(targetHeight, 1, ContractValidation.MaximumFrameDimension);

            using var source = new Bitmap(monitor.Width, monitor.Height, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(source))
            {
                graphics.CopyFromScreen(
                    monitor.Left,
                    monitor.Top,
                    0,
                    0,
                    new Size(monitor.Width, monitor.Height),
                    CopyPixelOperation.SourceCopy);
            }

            using var resized = ResizeWithAspect(source, targetWidth, targetHeight);
            using var output = new MemoryStream();
            using var parameters = new EncoderParameters(1);
            parameters.Param[0] = new EncoderParameter(
                Encoder.Quality,
                (long)Math.Clamp(jpegQuality, 30, 95));
            resized.Save(output, _jpegCodec, parameters);

            return ScreenFrame.Create(
                pcName,
                profile,
                monitor.Id,
                CaptureStream.Id,
                resized.Width,
                resized.Height,
                output.ToArray(),
                sequenceNumber) with
            {
                CaptureBackend = ScreenCaptureBackend.Gdi,
                JpegQuality = Math.Clamp(jpegQuality, 30, 95),
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Capture frame gagal");
            return null;
        }
    }

    private static Bitmap ResizeWithAspect(Bitmap source, int maximumWidth, int maximumHeight)
    {
        var ratio = Math.Min(
            (double)maximumWidth / source.Width,
            (double)maximumHeight / source.Height);
        var width = Math.Max(1, (int)(source.Width * ratio));
        var height = Math.Max(1, (int)(source.Height * ratio));
        var destination = new Bitmap(width, height, PixelFormat.Format24bppRgb);

        using var graphics = Graphics.FromImage(destination);
        graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
        graphics.DrawImage(source, 0, 0, width, height);
        return destination;
    }

    private static class CaptureStream
    {
        public static readonly string Id = Guid.NewGuid().ToString("N");
    }
}
