namespace LabKom.Shared.Contracts;

public enum FileCollectionRoot
{
    Desktop = 1,
    Documents = 2,
    Downloads = 3,
}

public enum FileCollectionState
{
    Collecting = 1,
    Completed = 2,
    Rejected = 3,
    Failed = 4,
}

/// <summary>
/// Permintaan satu file eksak dari folder pengguna yang dibatasi. Tidak
/// mendukung wildcard, path absolut, rekursi, atau folder sistem.
/// </summary>
public sealed record FileCollectionRequest(
    string RequestId,
    string TargetPcName,
    FileCollectionRoot Root,
    string RelativePath,
    long MaximumBytes,
    long RequestedAtUnixMs,
    long ExpiresAtUnixMs);

/// <summary>Chunk upload siswa ke Teacher; ukuran chunk dibatasi validator.</summary>
public sealed record FileCollectionChunk(
    string RequestId,
    string PcName,
    FileCollectionState State,
    int SequenceNumber,
    long TotalBytes,
    string FileName,
    byte[] Data,
    string? Sha256,
    string? Message,
    long TimestampUnixMs);
