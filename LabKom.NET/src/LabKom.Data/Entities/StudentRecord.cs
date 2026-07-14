using System.ComponentModel.DataAnnotations;

namespace LabKom.Data.Entities;

/// <summary>
/// Master data siswa. NIS sebagai kunci natural untuk login.
/// </summary>
public class StudentRecord
{
    public int Id { get; set; }

    [MaxLength(32)]
    public string Nis { get; set; } = string.Empty;

    [MaxLength(120)]
    public string FullName { get; set; } = string.Empty;

    [MaxLength(32)]
    public string Class { get; set; } = string.Empty;

    [MaxLength(120)]
    public string PasswordHash { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}
