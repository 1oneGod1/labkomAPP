using System.ComponentModel.DataAnnotations;

namespace LabKom.Data.Entities;

/// <summary>
/// Sesi login satu siswa di satu PC. Selesai saat LogoutAt terisi.
/// </summary>
public class Session
{
    public int Id { get; set; }

    [MaxLength(64)]
    public string PcName { get; set; } = string.Empty;

    public int StudentId { get; set; }
    public StudentRecord? Student { get; set; }

    public DateTime LoginAt { get; set; } = DateTime.UtcNow;
    public DateTime? LogoutAt { get; set; }

    [MaxLength(32)]
    public string? IpAddress { get; set; }

    [MaxLength(32)]
    public string? MacAddress { get; set; }

    public bool ForceLogout { get; set; }
}
