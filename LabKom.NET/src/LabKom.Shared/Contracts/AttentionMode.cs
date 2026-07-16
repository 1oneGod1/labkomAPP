namespace LabKom.Shared.Contracts;

/// <summary>Perintah Attention dengan identity dan TTL untuk mencegah replay stale.</summary>
public sealed record AttentionCommand(
    string CommandId,
    bool Enabled,
    string Message,
    string? TargetPcName,
    long IssuedAtUnixMs,
    long ExpiresAtUnixMs)
{
    public static AttentionCommand For(
        string? target,
        bool enabled,
        string message,
        TimeSpan? timeToLive = null)
    {
        var issued = DateTimeOffset.UtcNow;
        return new AttentionCommand(
            Guid.NewGuid().ToString("N"),
            enabled,
            message,
            target,
            issued.ToUnixTimeMilliseconds(),
            issued.Add(timeToLive ?? TimeSpan.FromSeconds(30)).ToUnixTimeMilliseconds());
    }
}
