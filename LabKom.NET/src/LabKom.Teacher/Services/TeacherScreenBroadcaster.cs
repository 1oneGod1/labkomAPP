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
/// Teacher screen publisher with explicit audience, pause/resume, one frame
/// in-flight, and per-broadcast sequence identity.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class TeacherScreenBroadcaster : IDisposable
{
    private const int TargetWidth = 1280;
    private const int TargetHeight = 720;
    private const int IntervalMs = 200;
    private const int JpegQuality = 65;

    private readonly HubContextHolder _hub;
    private readonly ILogger<TeacherScreenBroadcaster> _logger;
    private readonly ImageCodecInfo _jpegCodec;
    private readonly SemaphoreSlim _stateGate = new(1, 1);

    private CancellationTokenSource? _cancellation;
    private Task? _loopTask;
    private string _broadcastId = string.Empty;
    private string? _targetPcName;
    private long _sequenceNumber;
    private bool _isActive;
    private bool _isPaused;

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    public TeacherScreenBroadcaster(
        HubContextHolder hub,
        ILogger<TeacherScreenBroadcaster> logger)
    {
        _hub = hub;
        _logger = logger;
        _jpegCodec = ImageCodecInfo.GetImageEncoders()
            .First(codec => codec.FormatID == ImageFormat.Jpeg.Guid);
    }

    public bool IsActive => Volatile.Read(ref _isActive);
    public bool IsPaused => Volatile.Read(ref _isPaused);
    public string? TargetPcName => _targetPcName;

    public event EventHandler? StateChanged;

    public TeacherBroadcastSignal? BuildReplayFor(string pcName)
    {
        if (!IsActive
            || (_targetPcName is not null
                && !string.Equals(_targetPcName, pcName, StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        return new TeacherBroadcastSignal(_broadcastId, true, IsPaused);
    }

    public async Task StartAsync(string? targetPcName = null)
    {
        if (targetPcName is not null && !HubSecurity.IsValidPcName(targetPcName))
        {
            throw new ArgumentException("Target PC broadcast tidak valid.", nameof(targetPcName));
        }

        await _stateGate.WaitAsync();
        try
        {
            if (_isActive) return;

            _broadcastId = Guid.NewGuid().ToString("N");
            _targetPcName = targetPcName;
            _sequenceNumber = 0;
            Volatile.Write(ref _isPaused, false);
            Volatile.Write(ref _isActive, true);

            await SendSignalAsync(
                _broadcastId,
                _targetPcName,
                active: true,
                paused: false);

            _cancellation = new CancellationTokenSource();
            var token = _cancellation.Token;
            _loopTask = Task.Run(() => CaptureLoopAsync(token), token);
            RaiseStateChanged();

            _logger.LogInformation(
                "Teacher broadcast {BroadcastId} dimulai untuk {Target}",
                _broadcastId,
                _targetPcName ?? "semua Desktop");
        }
        finally
        {
            _stateGate.Release();
        }
    }

    public async Task TogglePauseAsync()
    {
        await _stateGate.WaitAsync();
        try
        {
            if (!_isActive) return;

            var paused = !_isPaused;
            Volatile.Write(ref _isPaused, paused);
            await SendSignalAsync(
                _broadcastId,
                _targetPcName,
                active: true,
                paused);
            RaiseStateChanged();
        }
        finally
        {
            _stateGate.Release();
        }
    }

    public async Task StopAsync()
    {
        await _stateGate.WaitAsync();
        try
        {
            if (!_isActive) return;

            _cancellation?.Cancel();
            try
            {
                if (_loopTask is not null) await _loopTask;
            }
            catch (OperationCanceledException)
            {
                // Expected during stop.
            }

            await SendSignalAsync(
                _broadcastId,
                _targetPcName,
                active: false,
                paused: false);

            _cancellation?.Dispose();
            _cancellation = null;
            _loopTask = null;
            Volatile.Write(ref _isPaused, false);
            Volatile.Write(ref _isActive, false);
            RaiseStateChanged();

            _logger.LogInformation(
                "Teacher broadcast {BroadcastId} dihentikan",
                _broadcastId);
        }
        finally
        {
            _stateGate.Release();
        }
    }

    private async Task CaptureLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (_isPaused)
                {
                    await Task.Delay(100, cancellationToken);
                    continue;
                }

                var sequence = Interlocked.Increment(ref _sequenceNumber);
                var frame = CaptureFrame(_broadcastId, sequence);
                if (frame is not null)
                {
                    await SendFrameAsync(frame, _targetPcName, cancellationToken);
                }

                await Task.Delay(IntervalMs, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Teacher capture loop error");
                await Task.Delay(500, cancellationToken);
            }
        }
    }

    private TeacherFrame? CaptureFrame(string broadcastId, long sequenceNumber)
    {
        try
        {
            var width = GetSystemMetrics(0);
            var height = GetSystemMetrics(1);
            if (width <= 0 || height <= 0) return null;

            using var source = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(source))
            {
                graphics.CopyFromScreen(
                    0,
                    0,
                    0,
                    0,
                    new Size(width, height),
                    CopyPixelOperation.SourceCopy);
            }

            var ratio = Math.Min(
                (double)TargetWidth / width,
                (double)TargetHeight / height);
            var destinationWidth = Math.Max(1, (int)(width * ratio));
            var destinationHeight = Math.Max(1, (int)(height * ratio));

            using var destination = new Bitmap(
                destinationWidth,
                destinationHeight,
                PixelFormat.Format24bppRgb);
            using (var graphics = Graphics.FromImage(destination))
            {
                graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
                graphics.DrawImage(source, 0, 0, destinationWidth, destinationHeight);
            }

            using var output = new MemoryStream();
            using var parameters = new EncoderParameters(1);
            parameters.Param[0] = new EncoderParameter(Encoder.Quality, (long)JpegQuality);
            destination.Save(output, _jpegCodec, parameters);

            return TeacherFrame.Create(
                broadcastId,
                sequenceNumber,
                destinationWidth,
                destinationHeight,
                output.ToArray());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Teacher screen capture gagal");
            return null;
        }
    }

    private Task SendFrameAsync(
        TeacherFrame frame,
        string? targetPcName,
        CancellationToken cancellationToken)
    {
        var hub = _hub.HubContext;
        if (hub is null) return Task.CompletedTask;

        return Audience(hub, targetPcName).SendAsync(
            HubRoutes.Methods.ReceiveTeacherFrame,
            frame,
            cancellationToken);
    }

    private Task SendSignalAsync(
        string broadcastId,
        string? targetPcName,
        bool active,
        bool paused)
    {
        var hub = _hub.HubContext;
        if (hub is null) return Task.CompletedTask;

        return Audience(hub, targetPcName).SendAsync(
            HubRoutes.Methods.ReceiveTeacherBroadcastSignal,
            new TeacherBroadcastSignal(broadcastId, active, paused));
    }

    private static IClientProxy Audience(
        IHubContext<LabKom.Teacher.Hub.TeacherHub> hub,
        string? targetPcName) =>
        targetPcName is null
            ? hub.Clients.Group(HubRoutes.Groups.ForRole(HubRoutes.Roles.Desktop))
            : hub.Clients.Group(
                HubRoutes.Groups.ForPcRole(targetPcName, HubRoutes.Roles.Desktop));

    private void RaiseStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);

    public void Dispose()
    {
        try
        {
            _cancellation?.Cancel();
        }
        catch
        {
            // Best effort during process exit.
        }

        _cancellation?.Dispose();
        _stateGate.Dispose();
    }
}
