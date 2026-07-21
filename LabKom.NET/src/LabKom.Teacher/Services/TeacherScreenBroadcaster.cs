using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using LabKom.Shared.Contracts;
using LabKom.Shared.Hub;
using LabKom.Shared.Security;
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
    private readonly TeacherAuthorizationService _authorization;

    private CancellationTokenSource? _cancellation;
    private Task? _loopTask;
    private string _broadcastId = string.Empty;
    private string[]? _targetPcNames;
    private long _sequenceNumber;
    private bool _isActive;
    private bool _isPaused;

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    public TeacherScreenBroadcaster(
        HubContextHolder hub,
        ILogger<TeacherScreenBroadcaster> logger,
        TeacherAuthorizationService authorization)
    {
        _hub = hub;
        _logger = logger;
        _authorization = authorization;
        _jpegCodec = ImageCodecInfo.GetImageEncoders()
            .First(codec => codec.FormatID == ImageFormat.Jpeg.Guid);
    }

    public bool IsActive => Volatile.Read(ref _isActive);
    public bool IsPaused => Volatile.Read(ref _isPaused);
    public IReadOnlyList<string>? TargetPcNames => _targetPcNames;
    public string AudienceLabel => _targetPcNames switch
    {
        null => "Semua siswa",
        { Length: 1 } targets => targets[0],
        { } targets => $"{targets.Length} PC terpilih",
    };

    public event EventHandler? StateChanged;

    public TeacherBroadcastSignal? BuildReplayFor(string pcName)
    {
        var targets = _targetPcNames;
        if (!IsActive
            || (targets is not null
                && !targets.Contains(
                    pcName,
                    StringComparer.OrdinalIgnoreCase)))
        {
            return null;
        }

        return new TeacherBroadcastSignal(_broadcastId, true, IsPaused);
    }

    public Task StartAsync(string? targetPcName = null) =>
        Authorized(
            "broadcast.start",
            targetPcName,
            () => StartCoreAsync(
                targetPcName is null
                    ? null
                    : NormalizeTargets(new[] { targetPcName })));

    public Task StartForTargetsAsync(IEnumerable<string> targetPcNames)
    {
        var targets = NormalizeTargets(targetPcNames);
        return Authorized(
            "broadcast.start.multiple",
            string.Join(",", targets),
            () => StartCoreAsync(targets));
    }

    public async Task TogglePauseAsync()
    {
        _authorization.Demand(TeacherPermission.BroadcastScreen, "broadcast.pause-toggle", AudienceLabel);
        await _stateGate.WaitAsync();
        try
        {
            if (!_isActive) return;

            var paused = !_isPaused;
            Volatile.Write(ref _isPaused, paused);
            await SendSignalAsync(
                _broadcastId,
                _targetPcNames,
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
        _authorization.Demand(TeacherPermission.BroadcastScreen, "broadcast.stop", AudienceLabel);
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
                _targetPcNames,
                active: false,
                paused: false);

            _cancellation?.Dispose();
            _cancellation = null;
            _loopTask = null;
            Volatile.Write(ref _isPaused, false);
            Volatile.Write(ref _isActive, false);
            _targetPcNames = null;
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

    private Task Authorized(string action, string? target, Func<Task> operation)
    {
        _authorization.Demand(
            TeacherPermission.BroadcastScreen,
            action,
            target);
        return operation();
    }

    private async Task StartCoreAsync(string[]? targetPcNames)
    {
        await _stateGate.WaitAsync();
        try
        {
            if (_isActive) return;

            _broadcastId = Guid.NewGuid().ToString("N");
            _targetPcNames = targetPcNames;
            _sequenceNumber = 0;
            Volatile.Write(ref _isPaused, false);
            Volatile.Write(ref _isActive, true);

            var signalSent = false;
            try
            {
                await SendSignalAsync(
                    _broadcastId,
                    _targetPcNames,
                    active: true,
                    paused: false);
                signalSent = true;

                _cancellation = new CancellationTokenSource();
                var token = _cancellation.Token;
                _loopTask = Task.Run(
                    () => CaptureLoopAsync(token),
                    token);
                RaiseStateChanged();

                _logger.LogInformation(
                    "Teacher broadcast {BroadcastId} dimulai untuk {Target}",
                    _broadcastId,
                    AudienceLabel);
            }
            catch
            {
                if (signalSent)
                {
                    try
                    {
                        await SendSignalAsync(
                            _broadcastId,
                            _targetPcNames,
                            active: false,
                            paused: false);
                    }
                    catch (Exception cleanupException)
                    {
                        _logger.LogDebug(
                            cleanupException,
                            "Rollback signal broadcast gagal");
                    }
                }

                _cancellation?.Cancel();
                _cancellation?.Dispose();
                _cancellation = null;
                _loopTask = null;
                _targetPcNames = null;
                Volatile.Write(ref _isPaused, false);
                Volatile.Write(ref _isActive, false);
                throw;
            }
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
                    await SendFrameAsync(
                        frame,
                        _targetPcNames,
                        cancellationToken);
                }

                await Task.Delay(IntervalMs, cancellationToken);
            }
            catch (OperationCanceledException) when (
                cancellationToken.IsCancellationRequested)
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

    private TeacherFrame? CaptureFrame(
        string broadcastId,
        long sequenceNumber)
    {
        try
        {
            var width = GetSystemMetrics(0);
            var height = GetSystemMetrics(1);
            if (width <= 0 || height <= 0) return null;

            using var source = new Bitmap(
                width,
                height,
                PixelFormat.Format32bppArgb);
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
                graphics.InterpolationMode =
                    InterpolationMode.HighQualityBilinear;
                graphics.DrawImage(
                    source,
                    0,
                    0,
                    destinationWidth,
                    destinationHeight);
            }

            using var output = new MemoryStream();
            using var parameters = new EncoderParameters(1);
            parameters.Param[0] = new EncoderParameter(
                Encoder.Quality,
                (long)JpegQuality);
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
        IReadOnlyCollection<string>? targetPcNames,
        CancellationToken cancellationToken)
    {
        var hub = _hub.HubContext;
        if (hub is null) return Task.CompletedTask;

        return Audience(hub, targetPcNames).SendAsync(
            HubRoutes.Methods.ReceiveTeacherFrame,
            frame,
            cancellationToken);
    }

    private Task SendSignalAsync(
        string broadcastId,
        IReadOnlyCollection<string>? targetPcNames,
        bool active,
        bool paused)
    {
        var hub = _hub.HubContext;
        if (hub is null) return Task.CompletedTask;

        return Audience(hub, targetPcNames).SendAsync(
            HubRoutes.Methods.ReceiveTeacherBroadcastSignal,
            new TeacherBroadcastSignal(broadcastId, active, paused));
    }

    private static IClientProxy Audience(
        IHubContext<LabKom.Teacher.Hub.TeacherHub> hub,
        IReadOnlyCollection<string>? targetPcNames)
    {
        if (targetPcNames is null)
        {
            return hub.Clients.Group(
                HubRoutes.Groups.ForRole(HubRoutes.Roles.Desktop));
        }

        var groups = targetPcNames
            .Select(pcName => HubRoutes.Groups.ForPcRole(
                pcName,
                HubRoutes.Roles.Desktop))
            .ToArray();
        return groups.Length == 1
            ? hub.Clients.Group(groups[0])
            : hub.Clients.Groups(groups);
    }

    private static string[] NormalizeTargets(
        IEnumerable<string> targetPcNames)
    {
        ArgumentNullException.ThrowIfNull(targetPcNames);
        var targets = targetPcNames
            .Select(pcName => pcName?.Trim() ?? string.Empty)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(pcName => pcName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (targets.Length == 0
            || targets.Any(pcName => !HubSecurity.IsValidPcName(pcName)))
        {
            throw new ArgumentException(
                "Audience broadcast wajib berisi nama PC yang valid.",
                nameof(targetPcNames));
        }

        return targets;
    }

    private void RaiseStateChanged() =>
        StateChanged?.Invoke(this, EventArgs.Empty);

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