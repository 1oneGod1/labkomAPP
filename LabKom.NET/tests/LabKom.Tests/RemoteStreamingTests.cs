using LabKom.Shared.Contracts;
using LabKom.Shared.Devices;
using LabKom.Student.Desktop.Services;
using LabKom.Student.Desktop.Services.Capture;

namespace LabKom.Tests;

public sealed class RemoteStreamingTests
{
    [Fact]
    public void AdaptiveController_DownshiftsOnPressureAndRecoversSlowly()
    {
        var controller = new AdaptiveStreamController();
        var initial = controller.GetPlan(
            CaptureProfile.Focus,
            1280,
            720,
            250,
            70);
        Assert.Equal(0, initial.AdaptationLevel);

        controller.Observe(
            CaptureProfile.Focus,
            400_000,
            180,
            220,
            delivered: true,
            maximumKilobitsPerSecond: 1_000,
            initial);
        controller.Observe(
            CaptureProfile.Focus,
            400_000,
            180,
            220,
            delivered: true,
            maximumKilobitsPerSecond: 1_000,
            initial);

        var reduced = controller.GetPlan(
            CaptureProfile.Focus,
            1280,
            720,
            250,
            70);
        Assert.Equal(1, reduced.AdaptationLevel);
        Assert.True(reduced.Width < initial.Width);
        Assert.True(reduced.JpegQuality < initial.JpegQuality);
        Assert.True(
            reduced.IntervalMilliseconds
            > initial.IntervalMilliseconds);

        for (var index = 0; index < 10; index++)
        {
            controller.Observe(
                CaptureProfile.Focus,
                8_000,
                5,
                5,
                delivered: true,
                maximumKilobitsPerSecond: 4_000,
                reduced);
        }

        var recovered = controller.GetPlan(
            CaptureProfile.Focus,
            1280,
            720,
            250,
            70);
        Assert.Equal(0, recovered.AdaptationLevel);
    }

    [Fact]
    public void RemoteSession_RejectsReplayWrongModeAndExpiredSession()
    {
        var identity = new MachineIdentity();
        var controller = new RemoteSessionController(identity);
        var control = RemoteSessionCommand.Start(
            identity.PcName,
            RemoteSessionMode.Control,
            monitorId: null);
        Assert.True(controller.TryApply(control));

        var first = RemoteInputCommand.Create(
            control,
            1,
            RemoteInputKind.MouseMove,
            32_767,
            32_767);
        Assert.True(controller.TryAccept(first, out var accepted));
        Assert.Equal(control.SessionId, accepted.Session.SessionId);
        Assert.False(controller.TryAccept(first, out _));

        Assert.True(controller.EndLocal(
            RemoteSessionState.EmergencyReleased,
            "test"));
        Assert.False(controller.TryAccept(
            RemoteInputCommand.Create(
                control,
                2,
                RemoteInputKind.KeyDown,
                virtualKey: 65),
            out _));

        var viewOnly = RemoteSessionCommand.Start(
            identity.PcName,
            RemoteSessionMode.ViewOnly,
            monitorId: null);
        Assert.True(controller.TryApply(viewOnly));
        Assert.False(controller.TryAccept(
            RemoteInputCommand.Create(
                viewOnly,
                1,
                RemoteInputKind.KeyDown,
                virtualKey: 65),
            out _));
    }

    [Fact]
    public void RemoteContracts_BindTargetAndValidateInputShape()
    {
        const string pcName = "LAB-PC-01";
        var session = RemoteSessionCommand.Start(
            pcName,
            RemoteSessionMode.Control,
            @"\\.\DISPLAY1");
        Assert.True(ContractValidation.IsValidRemoteSessionCommand(
            session,
            pcName));
        Assert.False(ContractValidation.IsValidRemoteSessionCommand(
            session,
            "LAB-PC-02"));

        var key = RemoteInputCommand.Create(
            session,
            1,
            RemoteInputKind.KeyDown,
            virtualKey: 65);
        Assert.True(ContractValidation.IsValidRemoteInput(key, pcName));
        Assert.False(ContractValidation.IsValidRemoteInput(
            key with { Button = RemoteMouseButton.Left },
            pcName));
        Assert.False(ContractValidation.IsValidRemoteInput(
            key with { SequenceNumber = 0 },
            pcName));
    }
}
