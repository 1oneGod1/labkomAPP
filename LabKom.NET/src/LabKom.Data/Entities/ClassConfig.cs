using System.ComponentModel.DataAnnotations;

namespace LabKom.Data.Entities;

/// <summary>
/// Konfigurasi key-value generik (web filter, blocked apps, attention default, dst).
/// Disimpan sebagai JSON string untuk fleksibilitas.
/// </summary>
public class ClassConfig
{
    [Key]
    [MaxLength(64)]
    public string Key { get; set; } = string.Empty;

    public string ValueJson { get; set; } = "{}";

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
