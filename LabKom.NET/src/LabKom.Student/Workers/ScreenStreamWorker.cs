using LabKom.Shared.Contracts;
using LabKom.Shared.Hub;
using LabKom.Student.Services;
using LabKom.Student.Services.Capture;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LabKom.Student.Workers;

/// <summary>
/// Loop capture screen + push frame via SignalR.
/// Interval & resolusi tergantung CaptureProfile saat ini.
/// </summary>
public class ScreenStreamWorker : BackgroundService
{
    private readonly IScreenCaptureSource _capture;
    private readonly HubConnectionService _hub;
    private readonly MachineIdentity _identity;
    private readonly CaptureProfileState _profileState;
    private readonly ILogger<ScreenStreamWorker> _logger;

    private readonly int _thumbW, _thumbH, _thumbInterval, _thumbQ;
    private readonly int _focusW, _focusH, _focusInterval, _focusQ;

    public ScreenStreamWorker(
        IScreenCaptureSource capture,
        HubConnectionService hub,
        MachineIdentity identity,
        CaptureProfileState profileState,
        IConfiguration config,
        ILogger<ScreenStreamWorker> logger)
    {
        _capture = capture;
        _hub = hub;
        _identity = identity;
        _profileState = profileState;
        _logger = logger;

        _thumbW = config.GetValue("Capture:ThumbnailWidth", 320);
        _thumbH = config.GetValue("Capture:ThumbnailHeight", 180);
        _thumbInterval = config.GetValue("Capture:ThumbnailIntervalMs", 1500);
        _thumbQ = config.GetValue("Capture:ThumbnailJpegQuality", 55);

        _focusW = config.GetValue("Capture:FocusWidth", 1280);
        _focusH = config.GetValue("Capture:FocusHeight", 720);
        _focusInterval = config.GetValue("Capture:FocusIntervalMs", 400);
        _focusQ = config.GetValue("Capture:FocusJpegQuality", 70);
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
                    await Task.Delay(1000, stoppingToken);
                    continue;
                }

                var profile = _profileState.Current;
                var (w, h, interval, q) = profile == CaptureProfile.Focus
                    ? (_focusW, _focusH, _focusInterval, _focusQ)
                    : (_thumbW, _thumbH, _thumbInterval, _thumbQ);

                var frame = _capture.CaptureFrame(_identity.PcName, profile, w, h, q);
                if (frame is not null)
                {
                    await _hub.PushScreenFrameAsync(frame, stoppingToken);
                }
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Screen stream loop error");
                await Task.Delay(1000, stoppingToken);
            }
        }
    }
}
