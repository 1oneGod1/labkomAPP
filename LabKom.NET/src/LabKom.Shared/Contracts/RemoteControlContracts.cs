namespace LabKom.Shared.Contracts;

/// <summary>Mode sesi remote yang terlihat oleh siswa.</summary>
public enum RemoteSessionMode
{
    ViewOnly = 0,
    Control = 1,
}

public enum RemoteInputKind
{
    MouseMove = 0,
    MouseButtonDown = 1,
    MouseButtonUp = 2,
    MouseWheel = 3,
    KeyDown = 4,
    KeyUp = 5,
}

public enum RemoteMouseButton
{
    None = 0,
    Left = 1,
    Right = 2,
    Middle = 3,
}

public enum RemoteSessionState
{
    Requested = 0,
    Active = 1,
    Rejected = 2,
    Ended = 3,
    Expired = 4,
    EmergencyReleased = 5,
}

/// <summary>
/// Sesi view/control berumur pendek. SessionId, target PC, mode, dan expiry
/// mengikat semua input agar replay atau salah-target ditolak Student Desktop.
/// </summary>
public sealed record RemoteSessionCommand(
    string SessionId,
    string TargetPcName,
    RemoteSessionMode Mode,
    string? MonitorId,
    bool Active,
    long IssuedAtUnixMs,
    long ExpiresAtUnixMs,
    string? Reason = null)
{
    public static RemoteSessionCommand Start(
        string targetPcName,
        RemoteSessionMode mode,
        string? monitorId,
        int ttlSeconds = 120)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var boundedTtl = Math.Clamp(
            ttlSeconds,
            15,
            ContractValidation.MaximumCommandTtlSeconds);
        return new RemoteSessionCommand(
            Guid.NewGuid().ToString("N"),
            targetPcName,
            mode,
            monitorId,
            true,
            now,
            now + boundedTtl * 1_000L);
    }

    public RemoteSessionCommand Renew(int ttlSeconds = 120)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var boundedTtl = Math.Clamp(
            ttlSeconds,
            15,
            ContractValidation.MaximumCommandTtlSeconds);
        return this with
        {
            Active = true,
            IssuedAtUnixMs = now,
            ExpiresAtUnixMs = now + boundedTtl * 1_000L,
            Reason = null,
        };
    }

    public RemoteSessionCommand Stop(string reason)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return this with
        {
            Active = false,
            IssuedAtUnixMs = now,
            ExpiresAtUnixMs = now + 30_000,
            Reason = reason,
        };
    }
}

/// <summary>Input remote bernomor urut; koordinat mouse relatif 0..65535 pada monitor sesi.</summary>
public sealed record RemoteInputCommand(
    string SessionId,
    string TargetPcName,
    long SequenceNumber,
    RemoteInputKind Kind,
    int NormalizedX,
    int NormalizedY,
    RemoteMouseButton Button,
    int WheelDelta,
    int VirtualKey,
    long TimestampUnixMs)
{
    public static RemoteInputCommand Create(
        RemoteSessionCommand session,
        long sequenceNumber,
        RemoteInputKind kind,
        int normalizedX = 0,
        int normalizedY = 0,
        RemoteMouseButton button = RemoteMouseButton.None,
        int wheelDelta = 0,
        int virtualKey = 0) =>
        new(
            session.SessionId,
            session.TargetPcName,
            sequenceNumber,
            kind,
            normalizedX,
            normalizedY,
            button,
            wheelDelta,
            virtualKey,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
}

public sealed record RemoteSessionStatus(
    string SessionId,
    string PcName,
    RemoteSessionState State,
    RemoteSessionMode Mode,
    long AcceptedInputCount,
    string? Message,
    long TimestampUnixMs)
{
    public static RemoteSessionStatus Create(
        RemoteSessionCommand session,
        RemoteSessionState state,
        long acceptedInputCount,
        string? message = null) =>
        new(
            session.SessionId,
            session.TargetPcName,
            state,
            session.Mode,
            Math.Max(0, acceptedInputCount),
            message,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
}

public static partial class ContractValidation
{
    public const int MaximumRemoteReasonLength = 256;
    public const int MaximumRemoteWheelDelta = 1_200;

    public static bool IsValidRemoteSessionCommand(
        RemoteSessionCommand? command,
        string expectedPcName,
        long? nowUnixMs = null)
    {
        if (command is null
            || !Guid.TryParseExact(command.SessionId, "N", out _)
            || !MatchesPc(command.TargetPcName, expectedPcName)
            || !Enum.IsDefined(command.Mode)
            || (command.MonitorId is not null
                && !IsValidMonitorText(command.MonitorId))
            || command.IssuedAtUnixMs <= 0
            || command.ExpiresAtUnixMs <= command.IssuedAtUnixMs
            || command.ExpiresAtUnixMs - command.IssuedAtUnixMs
                > MaximumCommandTtlSeconds * 1_000L
            || command.Reason?.Length > MaximumRemoteReasonLength)
        {
            return false;
        }

        var now = nowUnixMs ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return now >= command.IssuedAtUnixMs - MaximumClockSkewSeconds * 1_000L
               && now <= command.ExpiresAtUnixMs;
    }

    public static bool IsValidRemoteInput(
        RemoteInputCommand? input,
        string expectedPcName,
        long? nowUnixMs = null)
    {
        if (input is null
            || !Guid.TryParseExact(input.SessionId, "N", out _)
            || !MatchesPc(input.TargetPcName, expectedPcName)
            || input.SequenceNumber <= 0
            || !Enum.IsDefined(input.Kind)
            || !Enum.IsDefined(input.Button)
            || input.NormalizedX is < 0 or > 65_535
            || input.NormalizedY is < 0 or > 65_535
            || input.WheelDelta is < -MaximumRemoteWheelDelta
                or > MaximumRemoteWheelDelta
            || input.VirtualKey is < 0 or > 255
            || input.TimestampUnixMs <= 0)
        {
            return false;
        }

        var shapeIsValid = input.Kind switch
        {
            RemoteInputKind.MouseMove =>
                input.Button == RemoteMouseButton.None
                && input.WheelDelta == 0
                && input.VirtualKey == 0,
            RemoteInputKind.MouseButtonDown or RemoteInputKind.MouseButtonUp =>
                input.Button is RemoteMouseButton.Left
                    or RemoteMouseButton.Right
                    or RemoteMouseButton.Middle
                && input.WheelDelta == 0
                && input.VirtualKey == 0,
            RemoteInputKind.MouseWheel =>
                input.Button == RemoteMouseButton.None
                && input.WheelDelta != 0
                && input.VirtualKey == 0,
            RemoteInputKind.KeyDown or RemoteInputKind.KeyUp =>
                input.Button == RemoteMouseButton.None
                && input.WheelDelta == 0
                && input.VirtualKey is >= 8 and <= 254,
            _ => false,
        };
        if (!shapeIsValid) return false;

        var now = nowUnixMs ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return input.TimestampUnixMs
                   >= now - MaximumClockSkewSeconds * 1_000L
               && input.TimestampUnixMs
                   <= now + MaximumClockSkewSeconds * 1_000L;
    }

    public static bool IsValidRemoteSessionStatus(
        RemoteSessionStatus? status,
        string expectedPcName,
        long? nowUnixMs = null)
    {
        if (status is null
            || !Guid.TryParseExact(status.SessionId, "N", out _)
            || !MatchesPc(status.PcName, expectedPcName)
            || !Enum.IsDefined(status.State)
            || !Enum.IsDefined(status.Mode)
            || status.AcceptedInputCount < 0
            || status.Message?.Length > MaximumRemoteReasonLength
            || status.TimestampUnixMs <= 0)
        {
            return false;
        }

        var now = nowUnixMs ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return status.TimestampUnixMs
                   >= now - MaximumStateSnapshotAgeSeconds * 1_000L
               && status.TimestampUnixMs
                   <= now + MaximumClockSkewSeconds * 1_000L;
    }
}
