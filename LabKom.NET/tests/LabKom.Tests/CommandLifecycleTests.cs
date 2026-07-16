using LabKom.Shared.Contracts;
using LabKom.Teacher.Services;

namespace LabKom.Tests;

public sealed class CommandLifecycleTests
{
    [Fact]
    public void AttentionCommandRejectsWrongTargetAndExpiredReplay()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var command = AttentionCommand.For("PC-01", true, "Fokus");

        Assert.True(ContractValidation.IsValidAttentionCommand(command, "pc-01", now));
        Assert.False(ContractValidation.IsValidAttentionCommand(command, "PC-02", now));
        Assert.False(ContractValidation.IsValidAttentionCommand(
            command,
            "PC-01",
            command.ExpiresAtUnixMs + 1));
    }

    [Fact]
    public void PowerCommandHasBoundedDelayAndExpiry()
    {
        var command = PowerCommand.Restart(5, "Maintenance");

        Assert.True(ContractValidation.IsValidPowerCommand(command));
        Assert.False(ContractValidation.IsValidPowerCommand(
            command with { DelaySeconds = ContractValidation.MaximumPowerDelaySeconds + 1 }));
        Assert.False(ContractValidation.IsValidPowerCommand(
            command,
            command.ExpiresAtUnixMs + 1));
    }

    [Fact]
    public void CommandResultMustMatchConnectedPc()
    {
        var result = CommandResult.Create(
            Guid.NewGuid().ToString("N"),
            "PC-01",
            RemoteCommandKind.Attention,
            CommandExecutionState.Applied);

        Assert.True(ContractValidation.IsValidCommandResult(result, "pc-01"));
        Assert.False(ContractValidation.IsValidCommandResult(result, "PC-02"));
    }

    [Fact]
    public void IndividualUnlockOverridesGlobalLockOnReconnect()
    {
        var state = new AttentionStateStore();
        state.Apply(AttentionCommand.For(null, true, "Ujian"));
        state.Apply(AttentionCommand.For("PC-02", false, string.Empty));

        Assert.NotNull(state.BuildReplayFor("PC-01"));
        Assert.Null(state.BuildReplayFor("PC-02"));
    }

    [Fact]
    public void NewGlobalLockClearsOldPerPcOverrides()
    {
        var state = new AttentionStateStore();
        state.Apply(AttentionCommand.For("PC-02", false, string.Empty));
        state.Apply(AttentionCommand.For(null, true, "Kembali fokus"));

        var replay = Assert.IsType<AttentionCommand>(state.BuildReplayFor("PC-02"));
        Assert.True(replay.Enabled);
        Assert.Equal("Kembali fokus", replay.Message);
        Assert.Equal("PC-02", replay.TargetPcName);
    }
}
