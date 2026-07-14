namespace LabKom.Shared.Contracts;

/// <summary>
/// Perintah lock/blank-screen yang dikirim Teacher → Student.
/// Target null = broadcast ke semua student.
/// </summary>
public sealed record AttentionCommand(
    bool Enabled,
    string Message,
    string? TargetPcName,
    long TimestampUnixMs)
{
    public static AttentionCommand For(string? target, bool enabled, string message) => new(
        enabled,
        message,
        target,
        DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
}
