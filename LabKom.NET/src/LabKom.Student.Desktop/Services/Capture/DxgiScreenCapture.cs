using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using LabKom.Shared.Contracts;
using Microsoft.Extensions.Logging;
using SharpGen.Runtime;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using static Vortice.Direct3D11.D3D11;
using static Vortice.DXGI.DXGI;

namespace LabKom.Student.Desktop.Services.Capture;

/// <summary>
/// DXGI Desktop Duplication per output dengan fallback GDI. Resource duplikasi
/// di-reset saat driver/session menghasilkan access-lost dan dicoba lagi setelah cooldown.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DxgiScreenCapture : IScreenCaptureSource, IDisposable
{
    private const int DxgiErrorWaitTimeout = unchecked((int)0x887A0027);
    private static readonly TimeSpan RetryCooldown = TimeSpan.FromSeconds(15);

    private readonly object _gate = new();
    private readonly GdiScreenCapture _fallback;
    private readonly ILogger<DxgiScreenCapture> _logger;
    private readonly ImageCodecInfo _jpegCodec;
    private readonly Dictionary<string, DxgiOutputDuplicator> _outputs =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTimeOffset> _retryAfter =
        new(StringComparer.OrdinalIgnoreCase);

    public DxgiScreenCapture(
        GdiScreenCapture fallback,
        ILogger<DxgiScreenCapture> logger)
    {
        _fallback = fallback;
        _logger = logger;
        _jpegCodec = ImageCodecInfo.GetImageEncoders()
            .First(codec => codec.FormatID == ImageFormat.Jpeg.Guid);
    }

    public IReadOnlyList<MonitorDescriptor> GetMonitors() => _fallback.GetMonitors();

    public ScreenFrame? CaptureFrame(
        string pcName,
        CaptureProfile profile,
        string? monitorId,
        int targetWidth,
        int targetHeight,
        int jpegQuality,
        long sequenceNumber)
    {
        var monitors = GetMonitors();
        var monitor = monitors.FirstOrDefault(candidate =>
                          string.Equals(candidate.Id, monitorId, StringComparison.OrdinalIgnoreCase))
                      ?? monitors.FirstOrDefault(candidate => candidate.IsPrimary)
                      ?? monitors.FirstOrDefault();
        if (monitor is null) return null;

        lock (_gate)
        {
            try
            {
                if (_retryAfter.TryGetValue(monitor.Id, out var retry)
                    && retry > DateTimeOffset.UtcNow)
                {
                    return CaptureFallback(
                        pcName,
                        profile,
                        monitor.Id,
                        targetWidth,
                        targetHeight,
                        jpegQuality,
                        sequenceNumber);
                }

                var duplicator = GetOrCreate(monitor.Id);
                var stopwatch = Stopwatch.StartNew();
                using var source = duplicator.TryCapture();
                if (source is null) return null;

                using var resized = ResizeWithAspect(source, targetWidth, targetHeight);
                var jpeg = EncodeJpeg(resized, jpegQuality);
                stopwatch.Stop();
                return ScreenFrame.Create(
                    pcName,
                    profile,
                    monitor.Id,
                    StreamId.Value,
                    resized.Width,
                    resized.Height,
                    jpeg,
                    sequenceNumber) with
                {
                    CaptureBackend = ScreenCaptureBackend.DxgiDesktopDuplication,
                    JpegQuality = Math.Clamp(jpegQuality, 30, 95),
                    CaptureDurationMilliseconds = BoundedMilliseconds(stopwatch.Elapsed),
                };
            }
            catch (Exception exception)
            {
                ResetOutput(monitor.Id);
                _retryAfter[monitor.Id] = DateTimeOffset.UtcNow.Add(RetryCooldown);
                _logger.LogWarning(
                    exception,
                    "DXGI capture {MonitorId} gagal; beralih sementara ke GDI",
                    monitor.Id);
                return CaptureFallback(
                    pcName,
                    profile,
                    monitor.Id,
                    targetWidth,
                    targetHeight,
                    jpegQuality,
                    sequenceNumber);
            }
        }
    }

    private ScreenFrame? CaptureFallback(
        string pcName,
        CaptureProfile profile,
        string monitorId,
        int targetWidth,
        int targetHeight,
        int jpegQuality,
        long sequenceNumber) =>
        _fallback.CaptureFrame(
            pcName,
            profile,
            monitorId,
            targetWidth,
            targetHeight,
            jpegQuality,
            sequenceNumber);

    private DxgiOutputDuplicator GetOrCreate(string monitorId)
    {
        if (_outputs.TryGetValue(monitorId, out var current)) return current;

        using var factory = CreateDXGIFactory1<IDXGIFactory1>();
        for (uint adapterIndex = 0; ; adapterIndex++)
        {
            var adapterResult = factory.EnumAdapters1(adapterIndex, out var adapter);
            if (adapterResult.Failure) break;
            using (adapter)
            {
                for (uint outputIndex = 0; ; outputIndex++)
                {
                    var outputResult = adapter.EnumOutputs(outputIndex, out var output);
                    if (outputResult.Failure) break;
                    using (output)
                    {
                        var description = output.Description;
                        if (!description.AttachedToDesktop
                            || !string.Equals(
                                description.DeviceName,
                                monitorId,
                                StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        if (description.Rotation != ModeRotation.Identity)
                        {
                            throw new NotSupportedException(
                                "DXGI rotation non-identity memakai fallback GDI.");
                        }

                        var levels = new[]
                        {
                            FeatureLevel.Level_11_0,
                            FeatureLevel.Level_10_1,
                            FeatureLevel.Level_10_0,
                        };
                        var device = D3D11CreateDevice(
                            DriverType.Hardware,
                            DeviceCreationFlags.BgraSupport,
                            levels);
                        var context = device.ImmediateContext;
                        try
                        {
                            using var output1 = output.QueryInterface<IDXGIOutput1>();
                            var duplication = output1.DuplicateOutput(device);
                            current = new DxgiOutputDuplicator(
                                device,
                                context,
                                duplication);
                            _outputs.Add(monitorId, current);
                            _retryAfter.Remove(monitorId);
                            _logger.LogInformation(
                                "DXGI Desktop Duplication aktif untuk {MonitorId}",
                                monitorId);
                            return current;
                        }
                        catch
                        {
                            context.Dispose();
                            device.Dispose();
                            throw;
                        }
                    }
                }
            }
        }

        throw new InvalidOperationException(
            $"Output DXGI untuk {monitorId} tidak ditemukan.");
    }

    private byte[] EncodeJpeg(Bitmap bitmap, int jpegQuality)
    {
        using var output = new MemoryStream();
        using var parameters = new EncoderParameters(1);
        parameters.Param[0] = new EncoderParameter(
            Encoder.Quality,
            (long)Math.Clamp(jpegQuality, 30, 95));
        bitmap.Save(output, _jpegCodec, parameters);
        return output.ToArray();
    }

    private static Bitmap ResizeWithAspect(
        Bitmap source,
        int maximumWidth,
        int maximumHeight)
    {
        maximumWidth = Math.Clamp(
            maximumWidth,
            1,
            ContractValidation.MaximumFrameDimension);
        maximumHeight = Math.Clamp(
            maximumHeight,
            1,
            ContractValidation.MaximumFrameDimension);
        var ratio = Math.Min(
            (double)maximumWidth / source.Width,
            (double)maximumHeight / source.Height);
        var width = Math.Max(1, (int)(source.Width * ratio));
        var height = Math.Max(1, (int)(source.Height * ratio));
        var destination = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        using var graphics = Graphics.FromImage(destination);
        graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
        graphics.DrawImage(source, 0, 0, width, height);
        return destination;
    }

    private void ResetOutput(string monitorId)
    {
        if (!_outputs.Remove(monitorId, out var output)) return;
        output.Dispose();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            foreach (var output in _outputs.Values) output.Dispose();
            _outputs.Clear();
        }
    }

    private static int BoundedMilliseconds(TimeSpan elapsed) =>
        (int)Math.Clamp(elapsed.TotalMilliseconds, 0, 60_000);

    private sealed class DxgiOutputDuplicator : IDisposable
    {
        private readonly ID3D11Device _device;
        private readonly ID3D11DeviceContext _context;
        private readonly IDXGIOutputDuplication _duplication;

        public DxgiOutputDuplicator(
            ID3D11Device device,
            ID3D11DeviceContext context,
            IDXGIOutputDuplication duplication)
        {
            _device = device;
            _context = context;
            _duplication = duplication;
        }

        public Bitmap? TryCapture()
        {
            var result = _duplication.AcquireNextFrame(
                50,
                out _,
                out var desktopResource);
            if (result.Code == DxgiErrorWaitTimeout) return null;
            result.CheckError();

            try
            {
                using (desktopResource)
                using (var source = desktopResource.QueryInterface<ID3D11Texture2D>())
                {
                    var sourceDescription = source.Description;
                    if (sourceDescription.Format != Format.B8G8R8A8_UNorm)
                    {
                        throw new NotSupportedException(
                            $"Format DXGI {sourceDescription.Format} belum didukung.");
                    }

                    var stagingDescription = new Texture2DDescription
                    {
                        Width = sourceDescription.Width,
                        Height = sourceDescription.Height,
                        MipLevels = 1,
                        ArraySize = 1,
                        Format = sourceDescription.Format,
                        SampleDescription = new SampleDescription(1, 0),
                        Usage = ResourceUsage.Staging,
                        BindFlags = BindFlags.None,
                        CPUAccessFlags = CpuAccessFlags.Read,
                        MiscFlags = ResourceOptionFlags.None,
                    };
                    using var staging = _device.CreateTexture2D(stagingDescription);
                    _context.CopyResource(staging, source);

                    var mapResult = _context.Map(
                        staging,
                        0,
                        MapMode.Read,
                        Vortice.Direct3D11.MapFlags.None,
                        out var mapped);
                    mapResult.CheckError();
                    try
                    {
                        return CopyToBitmap(
                            mapped,
                            checked((int)sourceDescription.Width),
                            checked((int)sourceDescription.Height));
                    }
                    finally
                    {
                        _context.Unmap(staging, 0);
                    }
                }
            }
            finally
            {
                _duplication.ReleaseFrame();
            }
        }

        private static Bitmap CopyToBitmap(
            MappedSubresource mapped,
            int width,
            int height)
        {
            var bitmap = new Bitmap(
                width,
                height,
                PixelFormat.Format32bppArgb);
            var bounds = new Rectangle(0, 0, width, height);
            var destination = bitmap.LockBits(
                bounds,
                ImageLockMode.WriteOnly,
                PixelFormat.Format32bppArgb);
            try
            {
                var rowBytes = checked(width * 4);
                var row = new byte[rowBytes];
                for (var y = 0; y < height; y++)
                {
                    Marshal.Copy(
                        IntPtr.Add(mapped.DataPointer, checked((int)mapped.RowPitch * y)),
                        row,
                        0,
                        rowBytes);
                    Marshal.Copy(
                        row,
                        0,
                        IntPtr.Add(destination.Scan0, destination.Stride * y),
                        rowBytes);
                }
            }
            finally
            {
                bitmap.UnlockBits(destination);
            }

            return bitmap;
        }

        public void Dispose()
        {
            _duplication.Dispose();
            _context.Dispose();
            _device.Dispose();
        }
    }

    private static class StreamId
    {
        public static readonly string Value = Guid.NewGuid().ToString("N");
    }
}
