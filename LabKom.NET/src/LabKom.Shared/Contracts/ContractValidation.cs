namespace LabKom.Shared.Contracts;

/// <summary>Bounded validation for untrusted payloads received through SignalR.</summary>
public static class ContractValidation
{
    public const int MaximumFrameBytes = 1_572_864;
    public const int MaximumFrameDimension = 8_192;
    public const int MaximumMonitorCount = 16;
    public const int MaximumMonitorTextLength = 128;
    public const int MaximumVirtualCoordinate = 65_536;
    public const int MaximumActivityTitleLength = 512;
    public const int MaximumProcessNameLength = 128;
    public const int MaximumChatBodyLength = 2_000;
    public const int MaximumMessageIdLength = 64;
    public const int MaximumCommandMessageLength = 1_024;
    public const int MaximumPowerDelaySeconds = 3_600;
    public const int MaximumCommandTtlSeconds = 300;
    public const int MaximumClockSkewSeconds = 30;

    public static bool IsValidScreenFrame(ScreenFrame? frame, string expectedPcName)
    {
        if (frame is null || !MatchesPc(frame.PcName, expectedPcName)) return false;
        if (!Enum.IsDefined(frame.Profile)) return false;
        if (!IsValidMonitorText(frame.MonitorId)) return false;
        if (!Guid.TryParseExact(frame.StreamId, "N", out _)) return false;
        if (frame.SequenceNumber <= 0) return false;
        if (frame.Width is <= 0 or > MaximumFrameDimension) return false;
        if (frame.Height is <= 0 or > MaximumFrameDimension) return false;
        return frame.JpegData is { Length: > 0 and <= MaximumFrameBytes };
    }

    public static bool IsValidTeacherFrame(TeacherFrame? frame)
    {
        if (frame is null
            || !Guid.TryParseExact(frame.BroadcastId, "N", out _)
            || frame.SequenceNumber <= 0
            || frame.Width is <= 0 or > MaximumFrameDimension
            || frame.Height is <= 0 or > MaximumFrameDimension)
        {
            return false;
        }

        return frame.JpegData is { Length: > 0 and <= MaximumFrameBytes };
    }

    public static bool IsValidTeacherBroadcastSignal(TeacherBroadcastSignal? signal) =>
        signal is not null
        && Guid.TryParseExact(signal.BroadcastId, "N", out _)
        && (signal.Active || !signal.Paused);

    public static bool IsValidMonitorInventory(MonitorInventory? inventory, string expectedPcName)
    {
        if (inventory is null || !MatchesPc(inventory.PcName, expectedPcName)) return false;
        if (inventory.Monitors is null
            || inventory.Monitors.Count is < 1 or > MaximumMonitorCount)
        {
            return false;
        }

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var primaryCount = 0;
        foreach (var monitor in inventory.Monitors)
        {
            if (!IsValidMonitorText(monitor.Id)
                || !IsValidMonitorText(monitor.DeviceName)
                || !ids.Add(monitor.Id)
                || monitor.Width is <= 0 or > MaximumFrameDimension
                || monitor.Height is <= 0 or > MaximumFrameDimension
                || monitor.Left is < -MaximumVirtualCoordinate or > MaximumVirtualCoordinate
                || monitor.Top is < -MaximumVirtualCoordinate or > MaximumVirtualCoordinate)
            {
                return false;
            }

            if (monitor.IsPrimary) primaryCount++;
        }

        return primaryCount == 1;
    }

    public static bool IsValidCaptureProfileCommand(CaptureProfileCommand? command)
    {
        if (command is null || !Enum.IsDefined(command.Profile)) return false;
        return command.MonitorId is null || IsValidMonitorText(command.MonitorId);
    }

    public static bool IsValidChat(
        ChatMessage? message,
        string? expectedFromPc = null,
        ChatDirection? expectedDirection = null)
    {
        if (message is null || !Enum.IsDefined(message.Direction)) return false;
        if (expectedDirection is not null && message.Direction != expectedDirection) return false;
        if (message.Id.Length is < 1 or > MaximumMessageIdLength) return false;
        if (string.IsNullOrWhiteSpace(message.Body)
            || message.Body.Length > MaximumChatBodyLength
            || message.TimestampUnixMs <= 0)
        {
            return false;
        }

        return expectedFromPc is null || MatchesPc(message.FromPcName, expectedFromPc);
    }

    public static bool IsValidAttentionCommand(
        AttentionCommand? command,
        string expectedPcName,
        long? nowUnixMs = null)
    {
        if (command is null
            || !IsValidCommandWindow(
                command.CommandId,
                command.IssuedAtUnixMs,
                command.ExpiresAtUnixMs,
                nowUnixMs))
        {
            return false;
        }

        if (command.Message.Length > MaximumCommandMessageLength) return false;
        return command.TargetPcName is null || MatchesPc(command.TargetPcName, expectedPcName);
    }

    public static bool IsValidPowerCommand(PowerCommand? command, long? nowUnixMs = null)
    {
        if (command is null
            || !IsValidCommandWindow(
                command.CommandId,
                command.IssuedAtUnixMs,
                command.ExpiresAtUnixMs,
                nowUnixMs)
            || !Enum.IsDefined(command.Action)
            || command.DelaySeconds is < 0 or > MaximumPowerDelaySeconds)
        {
            return false;
        }

        return command.Reason is null || command.Reason.Length <= MaximumCommandMessageLength;
    }

    public static bool IsValidCommandResult(CommandResult? result, string expectedPcName)
    {
        if (result is null
            || !Guid.TryParseExact(result.CommandId, "N", out _)
            || !MatchesPc(result.PcName, expectedPcName)
            || !Enum.IsDefined(result.Kind)
            || !Enum.IsDefined(result.State)
            || result.TimestampUnixMs <= 0)
        {
            return false;
        }

        return result.Message is null || result.Message.Length <= MaximumCommandMessageLength;
    }

    public static bool IsValidActivity(ActivityRecord? record, string expectedPcName)
    {
        if (record is null || !MatchesPc(record.PcName, expectedPcName)) return false;
        if (!Enum.IsDefined(record.Kind)) return false;
        if (string.IsNullOrWhiteSpace(record.Title) || record.Title.Length > MaximumActivityTitleLength) return false;
        return record.ProcessName is null || record.ProcessName.Length <= MaximumProcessNameLength;
    }

    public static bool IsValidFileProgress(FileDistributionProgress? progress, string expectedPcName)
    {
        if (progress is null || !MatchesPc(progress.PcName, expectedPcName)) return false;
        if (!Enum.IsDefined(progress.State)) return false;
        if (progress.NoticeId.Length is < 1 or > 64 || progress.BytesReceived < 0) return false;
        return progress.ErrorMessage is null || progress.ErrorMessage.Length <= 1_024;
    }

    private static bool IsValidCommandWindow(
        string commandId,
        long issuedAtUnixMs,
        long expiresAtUnixMs,
        long? nowUnixMs)
    {
        if (!Guid.TryParseExact(commandId, "N", out _)
            || issuedAtUnixMs <= 0
            || expiresAtUnixMs <= issuedAtUnixMs
            || expiresAtUnixMs - issuedAtUnixMs > MaximumCommandTtlSeconds * 1_000L)
        {
            return false;
        }

        var now = nowUnixMs ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return now >= issuedAtUnixMs - MaximumClockSkewSeconds * 1_000L
               && now <= expiresAtUnixMs;
    }

    private static bool IsValidMonitorText(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= MaximumMonitorTextLength;

    private static bool MatchesPc(string? supplied, string expected) =>
        string.Equals(supplied, expected, StringComparison.OrdinalIgnoreCase);
}
