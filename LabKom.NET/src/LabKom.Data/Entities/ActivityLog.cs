using System.ComponentModel.DataAnnotations;

namespace LabKom.Data.Entities;

public enum ActivityKind
{
    WindowChange = 1,
    BrowserUrl = 2,
    AppLaunched = 3,
    AppClosed = 4,
    ChatSent = 5,
    Violation = 6,
    System = 7,
}

/// <summary>
/// Log aktivitas siswa per sesi. Dipakai untuk laporan & deteksi pelanggaran.
/// </summary>
public class ActivityLog
{
    public long Id { get; set; }

    public int? SessionId { get; set; }
    public Session? Session { get; set; }

    [MaxLength(64)]
    public string PcName { get; set; } = string.Empty;

    public ActivityKind Kind { get; set; }

    [MaxLength(256)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(1024)]
    public string? Detail { get; set; }

    public DateTime At { get; set; } = DateTime.UtcNow;
}
