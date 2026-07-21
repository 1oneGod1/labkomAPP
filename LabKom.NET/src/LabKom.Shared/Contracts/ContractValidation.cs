namespace LabKom.Shared.Contracts;

/// <summary>Bounded validation for untrusted payloads received through SignalR.</summary>
public static partial class ContractValidation
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
    public const int MaximumPolicyEntries = 256;
    public const int MaximumDomainLength = 253;
    public const int MaximumBlockedProcessNameLength = 128;
    public const int MaximumStateSnapshotAgeSeconds = 300;
    public const long MaximumTelemetryMemoryBytes = 1L << 50;
    public const long MaximumTelemetryDiskBytes = 1L << 60;
    public const long MaximumTelemetryNetworkBytesPerSecond = 100L * 1024 * 1024 * 1024;
    public const long MaximumTelemetryUptimeSeconds = 10L * 365 * 24 * 60 * 60;
    public const int MaximumTelemetryThreadCount = 100_000;
    public const long MaximumFileTransferBytes = 200L * 1024 * 1024;

    public static bool IsValidScreenFrame(ScreenFrame? frame, string expectedPcName)
    {
        if (frame is null || !MatchesPc(frame.PcName, expectedPcName)) return false;
        if (!Enum.IsDefined(frame.Profile)) return false;
        if (!IsValidMonitorText(frame.MonitorId)) return false;
        if (!Guid.TryParseExact(frame.StreamId, "N", out _)) return false;
        if (frame.SequenceNumber <= 0) return false;
        if (frame.Width is <= 0 or > MaximumFrameDimension) return false;
        if (frame.Height is <= 0 or > MaximumFrameDimension) return false;
        if (!Enum.IsDefined(frame.CaptureBackend)) return false;
        if (frame.JpegQuality is not (0 or >= 30 and <= 95)) return false;
        if (frame.TargetFramesPerSecond is < 0 or > 60) return false;
        if (frame.CaptureDurationMilliseconds is < 0 or > 60_000) return false;
        if (frame.PreviousSendDurationMilliseconds is < 0 or > 60_000)
        {
            return false;
        }

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

    public static bool IsValidClassroomStateSnapshot(
        ClassroomStateSnapshot? snapshot,
        string expectedPcName,
        long? nowUnixMs = null)
    {
        if (snapshot is null
            || !Guid.TryParseExact(snapshot.SessionId, "N", out _)
            || snapshot.TimestampUnixMs <= 0)
        {
            return false;
        }

        var now = nowUnixMs ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (snapshot.TimestampUnixMs < now - MaximumStateSnapshotAgeSeconds * 1_000L
            || snapshot.TimestampUnixMs > now + MaximumClockSkewSeconds * 1_000L)
        {
            return false;
        }

        if (snapshot.Attention is not null
            && (!snapshot.Attention.Enabled
                || !IsValidAttentionCommand(snapshot.Attention, expectedPcName, now)))
        {
            return false;
        }

        return snapshot.Broadcast is null
               || (snapshot.Broadcast.Active
                   && IsValidTeacherBroadcastSignal(snapshot.Broadcast));
    }
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

    public static bool IsValidWebFilterPolicy(WebFilterPolicy? policy, long? nowUnixMs = null)
    {
        if (policy is null
            || !IsValidCommandWindow(
                policy.CommandId,
                policy.IssuedAtUnixMs,
                policy.ExpiresAtUnixMs,
                nowUnixMs)
            || policy.Mode is not (WebFilterMode.Disabled or WebFilterMode.Blacklist)
            || policy.Domains is null
            || policy.Domains.Count > MaximumPolicyEntries)
        {
            return false;
        }

        if (policy.Mode == WebFilterMode.Disabled) return policy.Domains.Count == 0;
        if (policy.Domains.Count == 0) return false;

        var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return policy.Domains.All(domain =>
            IsValidDomain(domain) && unique.Add(domain));
    }

    public static bool IsValidAppBlockPolicy(AppBlockPolicy? policy, long? nowUnixMs = null)
    {
        if (policy is null
            || !IsValidCommandWindow(
                policy.CommandId,
                policy.IssuedAtUnixMs,
                policy.ExpiresAtUnixMs,
                nowUnixMs)
            || policy.ProcessNames is null
            || policy.ProcessNames.Count > MaximumPolicyEntries)
        {
            return false;
        }

        if (!policy.Enabled) return policy.ProcessNames.Count == 0;
        if (policy.ProcessNames.Count == 0) return false;

        var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return policy.ProcessNames.All(name =>
            IsValidProcessName(name) && unique.Add(name));
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
        if (record.ProcessName?.Length > MaximumProcessNameLength
            || record.TimestampUnixMs <= 0)
        {
            return false;
        }

        if (record.Kind == ActivityRecordKind.UsageSample)
        {
            return record.Metrics is
            {
                KeyboardEventCount: >= 0 and <= 20_000,
                IdleMilliseconds: >= 0 and <= 86_400_000,
            } metrics
            && Enum.IsDefined(metrics.Category);
        }

        return record.Metrics is null;
    }

    public static bool IsValidFileProgress(
        FileDistributionProgress? progress,
        string expectedPcName,
        long? nowUnixMs = null)
    {
        if (progress is null
            || !MatchesPc(progress.PcName, expectedPcName)
            || !Enum.IsDefined(progress.State)
            || !Guid.TryParseExact(progress.NoticeId, "N", out _)
            || progress.BytesReceived is < 0 or > MaximumFileTransferBytes
            || progress.TimestampUnixMs <= 0
            || (progress.State == FileDistributionState.Notified
                && progress.BytesReceived != 0)
            || (progress.State != FileDistributionState.Failed
                && progress.ErrorMessage is not null)
            || progress.ErrorMessage?.Length > MaximumCommandMessageLength)
        {
            return false;
        }

        var now = nowUnixMs ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return progress.TimestampUnixMs
                   >= now - MaximumStateSnapshotAgeSeconds * 1_000L
               && progress.TimestampUnixMs
                   <= now + MaximumClockSkewSeconds * 1_000L;
    }

    public static bool IsValidDeviceTelemetry(
        DeviceTelemetry? telemetry,
        string expectedPcName,
        long? nowUnixMs = null)
    {
        if (telemetry is null
            || !MatchesPc(telemetry.PcName, expectedPcName)
            || telemetry.SequenceNumber <= 0
            || telemetry.TimestampUnixMs <= 0
            || telemetry.UptimeSeconds is < 0 or > MaximumTelemetryUptimeSeconds
            || !double.IsFinite(telemetry.CpuPercent)
            || telemetry.CpuPercent is < 0 or > 100
            || telemetry.TotalMemoryBytes is <= 0 or > MaximumTelemetryMemoryBytes
            || telemetry.UsedMemoryBytes < 0
            || telemetry.UsedMemoryBytes > telemetry.TotalMemoryBytes
            || telemetry.DiskTotalBytes is <= 0 or > MaximumTelemetryDiskBytes
            || telemetry.DiskFreeBytes < 0
            || telemetry.DiskFreeBytes > telemetry.DiskTotalBytes
            || telemetry.NetworkReceiveBytesPerSecond is < 0
                or > MaximumTelemetryNetworkBytesPerSecond
            || telemetry.NetworkSendBytesPerSecond is < 0
                or > MaximumTelemetryNetworkBytesPerSecond
            || telemetry.AgentWorkingSetBytes is < 0
                or > MaximumTelemetryMemoryBytes
            || telemetry.AgentThreadCount is < 1
                or > MaximumTelemetryThreadCount)
        {
            return false;
        }

        var now = nowUnixMs ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return telemetry.TimestampUnixMs
                   >= now - MaximumStateSnapshotAgeSeconds * 1_000L
               && telemetry.TimestampUnixMs
                   <= now + MaximumClockSkewSeconds * 1_000L;
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

    private static bool IsValidDomain(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > MaximumDomainLength
            || value.Contains('/')
            || value.Contains(':')
            || value.Contains(' '))
        {
            return false;
        }

        return Uri.CheckHostName(value) == UriHostNameType.Dns;
    }

    private static bool IsValidProcessName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > MaximumBlockedProcessNameLength
            || value.Contains('\\')
            || value.Contains('/')
            || value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return value.All(character =>
            char.IsLetterOrDigit(character) || character is '.' or '_' or '-');
    }
    private static bool MatchesPc(string? supplied, string expected) =>
        string.Equals(supplied, expected, StringComparison.OrdinalIgnoreCase);
}
