using LabKom.Shared.Contracts;

namespace LabKom.Tests;

public sealed class ContractValidationTests
{
    [Fact]
    public void ScreenFrameMustMatchConnectedPcAndSizeLimits()
    {
        var valid = Frame("PC-01", 1);
        var oversized = valid with { JpegData = new byte[ContractValidation.MaximumFrameBytes + 1] };

        Assert.True(ContractValidation.IsValidScreenFrame(valid, "pc-01"));
        Assert.False(ContractValidation.IsValidScreenFrame(valid, "PC-02"));
        Assert.False(ContractValidation.IsValidScreenFrame(oversized, "PC-01"));
        Assert.False(ContractValidation.IsValidScreenFrame(valid with { Width = 0 }, "PC-01"));
        Assert.False(ContractValidation.IsValidScreenFrame(valid with { SequenceNumber = 0 }, "PC-01"));
        Assert.False(ContractValidation.IsValidScreenFrame(valid with { StreamId = "invalid" }, "PC-01"));
    }

    [Fact]
    public void TeacherBroadcastFramesRequireSessionIdentityAndSequence()
    {
        var broadcastId = Guid.NewGuid().ToString("N");
        var frame = TeacherFrame.Create(
            broadcastId,
            1,
            1280,
            720,
            new byte[256]);

        Assert.True(ContractValidation.IsValidTeacherFrame(frame));
        Assert.False(ContractValidation.IsValidTeacherFrame(
            frame with { SequenceNumber = 0 }));
        Assert.False(ContractValidation.IsValidTeacherFrame(
            frame with { BroadcastId = "invalid" }));
        Assert.True(ContractValidation.IsValidTeacherBroadcastSignal(
            new TeacherBroadcastSignal(broadcastId, true, true)));
        Assert.False(ContractValidation.IsValidTeacherBroadcastSignal(
            new TeacherBroadcastSignal(broadcastId, false, true)));
    }

    [Fact]
    public void MonitorInventoryRequiresUniqueIdsAndOnePrimary()
    {
        var primary = new MonitorDescriptor(
            "\\\\.\\DISPLAY1",
            "\\\\.\\DISPLAY1",
            0,
            0,
            1920,
            1080,
            true);
        var secondary = new MonitorDescriptor(
            "\\\\.\\DISPLAY2",
            "\\\\.\\DISPLAY2",
            1920,
            0,
            1920,
            1080,
            false);
        var valid = MonitorInventory.Snapshot("PC-01", new[] { primary, secondary });

        Assert.True(ContractValidation.IsValidMonitorInventory(valid, "pc-01"));
        Assert.False(ContractValidation.IsValidMonitorInventory(valid, "PC-02"));
        Assert.False(ContractValidation.IsValidMonitorInventory(
            valid with { Monitors = new[] { primary, secondary with { Id = primary.Id } } },
            "PC-01"));
        Assert.False(ContractValidation.IsValidMonitorInventory(
            valid with { Monitors = new[] { primary with { IsPrimary = false }, secondary } },
            "PC-01"));
    }

    [Fact]
    public void CaptureProfileCommandBoundsMonitorId()
    {
        Assert.True(ContractValidation.IsValidCaptureProfileCommand(
            new CaptureProfileCommand(CaptureProfile.Focus, "\\\\.\\DISPLAY2")));
        Assert.False(ContractValidation.IsValidCaptureProfileCommand(
            new CaptureProfileCommand(CaptureProfile.Focus, new string('x', 129))));
        Assert.False(ContractValidation.IsValidCaptureProfileCommand(
            new CaptureProfileCommand((CaptureProfile)999)));
    }

    [Fact]
    public void ChatPayloadIsBoundedAndBoundToSender()
    {
        var valid = new ChatMessage(
            Guid.NewGuid().ToString("N"),
            ChatDirection.StudentToTeacher,
            "PC-01",
            null,
            "Butuh bantuan",
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

        Assert.True(ContractValidation.IsValidChat(
            valid,
            "pc-01",
            ChatDirection.StudentToTeacher));
        Assert.False(ContractValidation.IsValidChat(valid, "PC-02"));
        Assert.False(ContractValidation.IsValidChat(
            valid with { Body = new string('x', ContractValidation.MaximumChatBodyLength + 1) }));
        Assert.False(ContractValidation.IsValidChat(
            valid with { Direction = ChatDirection.Broadcast },
            "PC-01",
            ChatDirection.StudentToTeacher));
    }

    [Fact]
    public void ActivityPayloadIsBoundedAndBoundToPc()
    {
        var valid = ActivityRecord.WindowChange("PC-01", "Browser", "chrome");
        var longTitle = valid with { Title = new string('x', ContractValidation.MaximumActivityTitleLength + 1) };

        Assert.True(ContractValidation.IsValidActivity(valid, "pc-01"));
        Assert.False(ContractValidation.IsValidActivity(valid, "PC-02"));
        Assert.False(ContractValidation.IsValidActivity(longTitle, "PC-01"));
    }

    private static ScreenFrame Frame(string pcName, long sequence) =>
        ScreenFrame.Create(
            pcName,
            CaptureProfile.Thumbnail,
            "\\\\.\\DISPLAY1",
            Guid.NewGuid().ToString("N"),
            480,
            270,
            new byte[256],
            sequence);
}
