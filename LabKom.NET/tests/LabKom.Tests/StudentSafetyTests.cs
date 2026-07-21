using LabKom.Shared.Security;
using LabKom.Student.Desktop.Services;
using LabKom.Student.Services;

namespace LabKom.Tests;

public sealed class StudentSafetyTests
{
    [Fact]
    public void WatchdogWaitsForGraceAndThrottlesRestartAttempts()
    {
        var state = new DesktopWatchdogState();
        var now = DateTimeOffset.UtcNow;
        var grace = TimeSpan.FromSeconds(10);
        var cooldown = TimeSpan.FromSeconds(30);

        Assert.False(state.ShouldAttemptStart(
            hasInteractiveSession: true,
            desktopRunning: false,
            now,
            grace,
            cooldown));
        Assert.True(state.ShouldAttemptStart(
            hasInteractiveSession: true,
            desktopRunning: false,
            now.Add(grace),
            grace,
            cooldown));
        Assert.False(state.ShouldAttemptStart(
            hasInteractiveSession: true,
            desktopRunning: false,
            now.AddSeconds(20),
            grace,
            cooldown));
        Assert.True(state.ShouldAttemptStart(
            hasInteractiveSession: true,
            desktopRunning: false,
            now.AddSeconds(40),
            grace,
            cooldown));
    }

    [Fact]
    public void WatchdogResetsMissingTimerWhenDesktopReturns()
    {
        var state = new DesktopWatchdogState();
        var now = DateTimeOffset.UtcNow;
        var grace = TimeSpan.FromSeconds(10);
        var cooldown = TimeSpan.FromSeconds(30);

        Assert.False(state.ShouldAttemptStart(true, false, now, grace, cooldown));
        Assert.False(state.ShouldAttemptStart(true, true, now.AddSeconds(5), grace, cooldown));
        Assert.False(state.ShouldAttemptStart(true, false, now.AddSeconds(6), grace, cooldown));
        Assert.False(state.ShouldAttemptStart(true, false, now.AddSeconds(15), grace, cooldown));
    }

    [Fact]
    public void AttentionRecoversAfterTeacherOfflineTimeout()
    {
        var state = new AttentionRecoveryState();
        var now = DateTimeOffset.UtcNow;
        state.ObserveTeacherConnection(connected: true, now);
        state.SetAttention(active: true, "command-1", now);
        state.ObserveTeacherConnection(connected: false, now.AddSeconds(5));

        Assert.Null(state.Evaluate(
            now.AddSeconds(34),
            TimeSpan.FromHours(2),
            TimeSpan.FromSeconds(30)));
        var decision = state.Evaluate(
            now.AddSeconds(35),
            TimeSpan.FromHours(2),
            TimeSpan.FromSeconds(30));

        Assert.NotNull(decision);
        Assert.Equal(
            AttentionRecoveryReason.TeacherOfflineTimeout,
            decision.Reason);
        Assert.Equal("command-1", decision.CommandId);
        Assert.False(state.IsAttentionActive);
        Assert.Null(state.Evaluate(
            now.AddMinutes(1),
            TimeSpan.FromHours(2),
            TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public void AttentionMaximumDurationWorksWhileTeacherConnected()
    {
        var state = new AttentionRecoveryState();
        var now = DateTimeOffset.UtcNow;
        state.ObserveTeacherConnection(connected: true, now);
        state.SetAttention(active: true, "command-2", now);

        var decision = state.Evaluate(
            now.AddMinutes(120),
            TimeSpan.FromMinutes(120),
            TimeSpan.FromMinutes(3));

        Assert.NotNull(decision);
        Assert.Equal(
            AttentionRecoveryReason.MaximumLockDuration,
            decision.Reason);
    }

    [Fact]
    public void EmergencyRecordIsMachineBoundAndTimeLimited()
    {
        var now = DateTimeOffset.UtcNow;
        var record = new EmergencyUnlockRecord(
            EmergencyUnlockStore.SchemaVersion,
            Environment.MachineName,
            now,
            now.AddMinutes(15),
            "LabAdmin",
            "Pemulihan lokal");

        Assert.True(EmergencyUnlockStore.IsActive(record, now.AddMinutes(1)));
        Assert.False(EmergencyUnlockStore.IsActive(record, now.AddMinutes(15)));
        Assert.Throws<InvalidDataException>(() =>
            EmergencyUnlockStore.IsActive(
                record with { MachineName = "OTHER-PC" },
                now));
    }
}
