using LabKom.Shared.Contracts;
using LabKom.Teacher.Services;

namespace LabKom.Tests;

public sealed class PresenceRegistryTests
{
    [Fact]
    public void LateDisconnectFromOldAgentDoesNotMarkReplacementOffline()
    {
        var registry = new PresenceRegistry();
        registry.Upsert(Presence("PC-01"), "agent-old");
        registry.Upsert(Presence("PC-01"), "agent-new");

        registry.MarkAgentDisconnected("agent-old");

        var current = Assert.IsType<StudentEntry>(registry.Get("PC-01"));
        Assert.Equal("agent-new", current.ConnectionId);
        Assert.Equal(StudentStatus.Online, current.Status);
    }

    [Fact]
    public void CurrentAgentDisconnectClearsFrameAndMarksOffline()
    {
        var registry = new PresenceRegistry();
        registry.Upsert(Presence("PC-01"), "agent-current");
        registry.RegisterDesktop("PC-01", "desktop-current");
        Assert.True(registry.UpdateFrame(Frame("stream-a", 1), "desktop-current"));

        registry.MarkAgentDisconnected("agent-current");

        var current = Assert.IsType<StudentEntry>(registry.Get("PC-01"));
        Assert.Equal(StudentStatus.Offline, current.Status);
        Assert.Null(current.LastFrame);
    }

    [Fact]
    public void LateFramesFromReplacedDesktopAreRejected()
    {
        var registry = new PresenceRegistry();
        registry.Upsert(Presence("PC-01"), "agent-current");
        registry.RegisterDesktop("PC-01", "desktop-old");
        Assert.True(registry.UpdateFrame(Frame("stream-old", 10), "desktop-old"));

        registry.RegisterDesktop("PC-01", "desktop-new");

        Assert.False(registry.UpdateFrame(Frame("stream-old", 11), "desktop-old"));
        Assert.True(registry.UpdateFrame(Frame("stream-new", 1), "desktop-new"));
        Assert.Equal(
            StreamId("stream-new"),
            Assert.IsType<StudentEntry>(registry.Get("PC-01")).LastFrame?.StreamId);
    }

    [Fact]
    public void ReorderedFramesWithinStreamAreRejected()
    {
        var registry = new PresenceRegistry();
        registry.Upsert(Presence("PC-01"), "agent-current");
        registry.RegisterDesktop("PC-01", "desktop-current");

        Assert.True(registry.UpdateFrame(Frame("stream-current", 2), "desktop-current"));
        Assert.False(registry.UpdateFrame(Frame("stream-current", 1), "desktop-current"));
        Assert.Equal(
            2,
            Assert.IsType<StudentEntry>(registry.Get("PC-01")).LastFrame?.SequenceNumber);
    }

    [Fact]
    public void InventoryBeforeAgentPresenceIsAppliedAfterHello()
    {
        var registry = new PresenceRegistry();
        registry.RegisterDesktop("PC-01", "desktop-current");
        var inventory = MonitorInventory.Snapshot(
            "PC-01",
            new[]
            {
                new MonitorDescriptor("\\\\.\\DISPLAY1", "\\\\.\\DISPLAY1", 0, 0, 1920, 1080, true),
            });

        Assert.True(registry.UpdateMonitorInventory(inventory, "desktop-current"));
        registry.Upsert(Presence("PC-01"), "agent-current");

        var current = Assert.IsType<StudentEntry>(registry.Get("PC-01"));
        Assert.Same(inventory, current.MonitorInventory);
        Assert.Equal("desktop-current", current.DesktopConnectionId);
    }

    private static StudentPresence Presence(string pcName) =>
        StudentPresence.Snapshot(
            pcName,
            "00:11:22:33:44:55",
            "10.10.10.2",
            StudentStatus.Online);

    private static ScreenFrame Frame(string streamId, long sequence) =>
        ScreenFrame.Create(
            "PC-01",
            CaptureProfile.Thumbnail,
            "\\\\.\\DISPLAY1",
            StreamId(streamId),
            480,
            270,
            new byte[64],
            sequence);

    private static string StreamId(string seed) =>
        Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(seed)))[..32]
            .ToLowerInvariant();
}
