using LabKom.Shared.Security;
using LabKom.Student.Desktop.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LabKom.Student.Desktop.Workers;

/// <summary>
/// Releases managed UI after a bounded lock failure and honors a short-lived,
/// administrator-created local emergency override.
/// </summary>
public sealed class AttentionRecoveryWorker : BackgroundService
{
    private readonly DesktopHubClient _hub;
    private readonly AttentionRecoveryState _state;
    private readonly ILogger<AttentionRecoveryWorker> _logger;
    private readonly TimeSpan _maximumLockDuration;
    private readonly TimeSpan _teacherOfflineTimeout;
    private readonly TimeSpan _pollInterval;
    private DateTimeOffset? _observedEmergencyIssuedAt;
    private bool _emergencyWasActive;

    public event EventHandler<AttentionRecoveryDecision>? RecoveryTriggered;

    public AttentionRecoveryWorker(
        DesktopHubClient hub,
        AttentionRecoveryState state,
        IConfiguration configuration,
        ILogger<AttentionRecoveryWorker> logger)
    {
        _hub = hub;
        _state = state;
        _logger = logger;
        _maximumLockDuration = TimeSpan.FromMinutes(
            Math.Clamp(
                configuration.GetValue("Recovery:MaximumLockMinutes", 120),
                1,
                24 * 60));
        _teacherOfflineTimeout = TimeSpan.FromSeconds(
            Math.Clamp(
                configuration.GetValue("Recovery:TeacherOfflineUnlockSeconds", 180),
                30,
                24 * 60 * 60));
        _pollInterval = TimeSpan.FromMilliseconds(
            Math.Clamp(
                configuration.GetValue("Recovery:PollIntervalMilliseconds", 1000),
                250,
                10_000));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var now = DateTimeOffset.UtcNow;
                _state.ObserveTeacherConnection(_hub.IsConnected, now);

                if (EmergencyUnlockStore.TryGetActive(now, out var emergency))
                {
                    _emergencyWasActive = true;
                    if (_observedEmergencyIssuedAt != emergency.IssuedAtUtc)
                    {
                        _observedEmergencyIssuedAt = emergency.IssuedAtUtc;
                        Trigger(_state.ForceEmergency(
                            now,
                            $"Emergency unlock oleh {emergency.IssuedBy}: {emergency.Reason}"));
                    }
                }
                else
                {
                    if (_emergencyWasActive)
                    {
                        _emergencyWasActive = false;
                        _observedEmergencyIssuedAt = null;
                        await _hub.ResetConnectionAsync(stoppingToken);
                    }

                    var decision = _state.Evaluate(
                        now,
                        _maximumLockDuration,
                        _teacherOfflineTimeout);
                    if (decision is not null) Trigger(decision);
                }

                await Task.Delay(_pollInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Recovery monitor Student Desktop gagal");
                await Task.Delay(_pollInterval, stoppingToken);
            }
        }
    }

    private void Trigger(AttentionRecoveryDecision decision)
    {
        _logger.LogWarning(
            "Student Desktop dilepas oleh recovery: {Reason} ({Description})",
            decision.Reason,
            decision.Description);
        RecoveryTriggered?.Invoke(this, decision);
    }
}
