using System.Diagnostics;
using LabKom.Shared.Contracts;
using LabKom.Shared.Devices;
using LabKom.Student.Desktop.Services;
using LabKom.Student.Desktop.Services.Capture;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LabKom.Student.Desktop.Workers;

/// <summary>
/// Capture loop dengan satu frame in-flight. Profile, resolusi, interval, dan
/// monitor dapat diubah Teacher tanpa memulai ulang Student Desktop.
/// </summary>
public sealed class ScreenStreamWorker : BackgroundService
{
    private static readonly TimeSpan InventoryRefreshInterval = TimeSpan.FromSeconds(30);

    private readonly IScreenCaptureSource _capture;
    private readonly DesktopHubClient _hub;
    private readonly MachineIdentity _identity;
    private readonly CaptureProfileState _profileState;
    private readonly AdaptiveStreamController _adaptive;
    private readonly ILogger<ScreenStreamWorker> _logger;

    private readonly int _thumbnailWidth;
    private readonly int _thumbnailHeight;
    private readonly int _thumbnailInterval;
    private readonly int _thumbnailQuality;
    private readonly int _focusWidth;
    private readonly int _focusHeight;
    private readonly int _focusInterval;
    private readonly int _focusQuality;
    private readonly int _thumbnailMaximumKbps;
    private readonly int _focusMaximumKbps;
    private int _previousSendMilliseconds;

    private DateTimeOffset _nextInventoryUtc = DateTimeOffset.MinValue;
    private string _lastInventoryFingerprint = string.Empty;
    private long _sequenceNumber;

    public ScreenStreamWorker(
        IScreenCaptureSource capture,
        DesktopHubClient hub,
        MachineIdentity identity,
        CaptureProfileState profileState,
        IConfiguration config,
        AdaptiveStreamController adaptive,
        ILogger<ScreenStreamWorker> logger)
    {
        _capture = capture;
        _hub = hub;
        _identity = identity;
        _profileState = profileState;
        _logger = logger;
        _adaptive = adaptive;

        _thumbnailWidth = BoundedDimension(config.GetValue("Capture:ThumbnailWidth", 480));
        _thumbnailHeight = BoundedDimension(config.GetValue("Capture:ThumbnailHeight", 270));
        _thumbnailInterval = Math.Clamp(config.GetValue("Capture:ThumbnailIntervalMs", 1_000), 100, 60_000);
        _thumbnailQuality = Math.Clamp(config.GetValue("Capture:ThumbnailJpegQuality", 55), 30, 95);

        _focusWidth = BoundedDimension(config.GetValue("Capture:FocusWidth", 1280));
        _focusHeight = BoundedDimension(config.GetValue("Capture:FocusHeight", 720));
        _focusInterval = Math.Clamp(config.GetValue("Capture:FocusIntervalMs", 250), 100, 60_000);
        _focusQuality = Math.Clamp(config.GetValue("Capture:FocusJpegQuality", 70), 30, 95);
        _thumbnailMaximumKbps = Math.Clamp(config.GetValue("Capture:ThumbnailMaximumKbps", 750), 64, 100_000);
        _focusMaximumKbps = Math.Clamp(config.GetValue("Capture:FocusMaximumKbps", 4_000), 64, 100_000);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ScreenStreamWorker dimulai");
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!_hub.IsConnected)
                {
                    await Task.Delay(1_000, stoppingToken);
                    continue;
                }

                await PublishMonitorInventoryIfNeededAsync(stoppingToken);

                var loopStopwatch = Stopwatch.StartNew();
                var selection = _profileState.Current;
                var (width, height, interval, quality, maximumKbps) =
                    selection.Profile == CaptureProfile.Focus
                        ? (
                            _focusWidth,
                            _focusHeight,
                            _focusInterval,
                            _focusQuality,
                            _focusMaximumKbps)
                        : (
                            _thumbnailWidth,
                            _thumbnailHeight,
                            _thumbnailInterval,
                            _thumbnailQuality,
                            _thumbnailMaximumKbps);
                var plan = _adaptive.GetPlan(
                    selection.Profile,
                    width,
                    height,
                    interval,
                    quality);

                var sequence = Interlocked.Increment(ref _sequenceNumber);
                var captureStopwatch = Stopwatch.StartNew();
                var frame = _capture.CaptureFrame(
                    _identity.PcName,
                    selection.Profile,
                    selection.MonitorId,
                    plan.Width,
                    plan.Height,
                    plan.JpegQuality,
                    sequence);
                captureStopwatch.Stop();
                if (frame is not null)
                {
                    var captureMilliseconds = (int)Math.Clamp(
                        captureStopwatch.Elapsed.TotalMilliseconds,
                        0,
                        60_000);
                    frame = frame with
                    {
                        JpegQuality = plan.JpegQuality,
                        TargetFramesPerSecond = plan.TargetFramesPerSecond,
                        CaptureDurationMilliseconds = captureMilliseconds,
                        PreviousSendDurationMilliseconds = _previousSendMilliseconds,
                    };
                    var sendMilliseconds = await _hub.PushScreenFrameMeasuredAsync(
                        frame,
                        stoppingToken);
                    _adaptive.Observe(
                        selection.Profile,
                        frame.JpegData.Length,
                        captureMilliseconds,
                        sendMilliseconds ?? 60_000,
                        sendMilliseconds.HasValue,
                        maximumKbps,
                        plan);
                    _previousSendMilliseconds = sendMilliseconds ?? 60_000;
                }

                var remainingDelay = plan.IntervalMilliseconds
                                     - (int)loopStopwatch.ElapsedMilliseconds;
                if (remainingDelay > 0)
                {
                    await Task.Delay(remainingDelay, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Screen stream loop error");
                await Task.Delay(1_000, stoppingToken);
            }
        }
    }

    private async Task PublishMonitorInventoryIfNeededAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var monitors = _capture.GetMonitors();
        if (monitors.Count == 0) return;

        var fingerprint = string.Join(
            "|",
            monitors.Select(monitor =>
                $"{monitor.Id}:{monitor.Left}:{monitor.Top}:{monitor.Width}:{monitor.Height}:{monitor.IsPrimary}"));
        if (now < _nextInventoryUtc && fingerprint == _lastInventoryFingerprint) return;

        _lastInventoryFingerprint = fingerprint;
        _nextInventoryUtc = now.Add(InventoryRefreshInterval);
        await _hub.PushMonitorInventoryAsync(
            MonitorInventory.Snapshot(_identity.PcName, monitors),
            cancellationToken);
    }

    private static int BoundedDimension(int value) =>
        Math.Clamp(value, 1, ContractValidation.MaximumFrameDimension);
}
