using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using LabKom.Shared.Contracts;
using LabKom.Shared.Hub;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace LabKom.Teacher.Services;

/// <summary>
/// Saat aktif: capture layar guru periodik (5 fps) lalu push frame
/// ke semua overlay client. Saat berhenti: kirim sinyal supaya overlay
/// menyembunyikan window broadcast.
/// </summary>
[SupportedOSPlatform("windows")]
public class TeacherScreenBroadcaster : IDisposable
{
    private readonly HubContextHolder _hub;
    private readonly ILogger<TeacherScreenBroadcaster> _logger;
    private readonly ImageCodecInfo _jpegCodec;

    private CancellationTokenSource? _cts;
    private Task? _loopTask;
    private const int TargetWidth = 1280;
    private const int TargetHeight = 720;
    private const int IntervalMs = 200;
    private const int JpegQuality = 65;

    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int nIndex);

    public bool IsActive { get; private set; }
    public event EventHandler<bool>? ActiveChanged;

    public TeacherScreenBroadcaster(HubContextHolder hub, ILogger<TeacherScreenBroadcaster> logger)
    {
        _hub = hub;
        _logger = logger;
        _jpegCodec = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
    }

    public async Task StartAsync()
    {
        if (IsActive) return;
        IsActive = true;
        ActiveChanged?.Invoke(this, true);

        await SendSignalAsync(active: true);

        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _loopTask = Task.Run(() => CaptureLoop(token), token);
        _logger.LogInformation("Teacher broadcast dimulai");
    }

    public async Task StopAsync()
    {
        if (!IsActive) return;
        IsActive = false;
        ActiveChanged?.Invoke(this, false);

        _cts?.Cancel();
        try { if (_loopTask is not null) await _loopTask; } catch { }
        _cts?.Dispose();
        _cts = null;
        _loopTask = null;

        await SendSignalAsync(active: false);
        _logger.LogInformation("Teacher broadcast dihentikan");
    }

    private async Task CaptureLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                var frame = CaptureFrame();
                if (frame is not null)
                {
                    await SendFrameAsync(frame);
                }
                await Task.Delay(IntervalMs, token);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Capture loop guru error");
                await Task.Delay(500, token);
            }
        }
    }

    private TeacherFrame? CaptureFrame()
    {
        try
        {
            var w = GetSystemMetrics(0);
            var h = GetSystemMetrics(1);
            if (w <= 0 || h <= 0) return null;

            using var src = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(src))
            {
                g.CopyFromScreen(0, 0, 0, 0, new Size(w, h), CopyPixelOperation.SourceCopy);
            }

            var ratio = Math.Min((double)TargetWidth / w, (double)TargetHeight / h);
            var dw = Math.Max(1, (int)(w * ratio));
            var dh = Math.Max(1, (int)(h * ratio));

            using var dst = new Bitmap(dw, dh, PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(dst))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                g.DrawImage(src, 0, 0, dw, dh);
            }

            using var ms = new MemoryStream();
            var ep = new EncoderParameters(1);
            ep.Param[0] = new EncoderParameter(Encoder.Quality, (long)JpegQuality);
            dst.Save(ms, _jpegCodec, ep);

            return TeacherFrame.Create(dw, dh, ms.ToArray());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "CaptureFrame guru gagal");
            return null;
        }
    }

    private Task SendFrameAsync(TeacherFrame frame)
    {
        var hub = _hub.HubContext;
        if (hub is null) return Task.CompletedTask;
        return hub.Clients.All.SendAsync(HubRoutes.Methods.ReceiveTeacherFrame, frame);
    }

    private Task SendSignalAsync(bool active)
    {
        var hub = _hub.HubContext;
        if (hub is null) return Task.CompletedTask;
        return hub.Clients.All.SendAsync(
            HubRoutes.Methods.ReceiveTeacherBroadcastSignal,
            new TeacherBroadcastSignal(active));
    }

    public void Dispose()
    {
        try { _cts?.Cancel(); } catch { }
        _cts?.Dispose();
    }
}
