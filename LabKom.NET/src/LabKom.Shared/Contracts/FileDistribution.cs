namespace LabKom.Shared.Contracts;

public enum FileDistributionState
{
    Notified = 0,
    Downloading = 1,
    Completed = 2,
    Failed = 3,
}

/// <summary>
/// Pemberitahuan dari guru bahwa ada file siap diunduh siswa.
/// DownloadUrl menunjuk ke endpoint static Kestrel di Teacher Console.
/// </summary>
public sealed record FileDistributionNotice(
    string Id,
    string FileName,
    long SizeBytes,
    string Sha256,
    string DownloadUrl,
    string? TargetPcName,
    long TimestampUnixMs)
{
    public static FileDistributionNotice Create(
        string fileName, long size, string sha256, string url, string? target) => new(
            Guid.NewGuid().ToString("N"),
            fileName,
            size,
            sha256,
            url,
            target,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
}

/// <summary>
/// Status download yang dilaporkan kembali oleh siswa untuk progress UI di guru.
/// </summary>
public sealed record FileDistributionProgress(
    string NoticeId,
    string PcName,
    FileDistributionState State,
    long BytesReceived,
    string? ErrorMessage,
    long TimestampUnixMs);
