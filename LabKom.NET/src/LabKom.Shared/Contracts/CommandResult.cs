namespace LabKom.Shared.Contracts;

public enum RemoteCommandKind
{
    Attention = 1,
    Power = 2,
    WebFilter = 3,
    AppBlock = 4,
}

public enum CommandExecutionState
{
    Accepted = 1,
    Applied = 2,
    Rejected = 3,
    Failed = 4,
}

public sealed record CommandResult(
    string CommandId,
    string PcName,
    RemoteCommandKind Kind,
    CommandExecutionState State,
    string? Message,
    long TimestampUnixMs)
{
    public static CommandResult Create(
        string commandId,
        string pcName,
        RemoteCommandKind kind,
        CommandExecutionState state,
        string? message = null) => new(
            commandId,
            pcName,
            kind,
            state,
            message,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
}
