using System.IO;
using System.Text.Json;
using LabKom.Shared.Hub;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace LabKom.Teacher.Services;

public sealed record ClassroomGroupDefinition(
    string Name,
    IReadOnlyList<string> PcNames);

/// <summary>Persistent Teacher-local room groups with bounded validation.</summary>
public sealed class ClassroomGroupStore
{
    private const int MaximumGroups = 128;
    private const int MaximumGroupNameLength = 64;
    private const int MaximumMembersPerGroup = 256;

    private readonly object _sync = new();
    private readonly string _storagePath;
    private readonly ILogger<ClassroomGroupStore>? _logger;
    private List<ClassroomGroupDefinition> _groups;

    public ClassroomGroupStore(
        IConfiguration configuration,
        ILogger<ClassroomGroupStore> logger)
        : this(ResolveStoragePath(configuration), logger)
    {
    }

    public ClassroomGroupStore(string storagePath)
        : this(storagePath, logger: null)
    {
    }

    private ClassroomGroupStore(
        string storagePath,
        ILogger<ClassroomGroupStore>? logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storagePath);
        _storagePath = Path.GetFullPath(
            Environment.ExpandEnvironmentVariables(storagePath));
        _logger = logger;
        _groups = Load();
    }

    public IReadOnlyList<ClassroomGroupDefinition> Snapshot()
    {
        lock (_sync)
        {
            return _groups
                .Select(group => group with
                {
                    PcNames = group.PcNames.ToArray(),
                })
                .ToArray();
        }
    }

    public ClassroomGroupDefinition Upsert(
        string name,
        IEnumerable<string> pcNames)
    {
        var group = Normalize(name, pcNames);
        lock (_sync)
        {
            var updated = _groups.ToList();
            var existing = updated.FindIndex(candidate =>
                string.Equals(
                    candidate.Name,
                    group.Name,
                    StringComparison.OrdinalIgnoreCase));
            if (existing >= 0)
            {
                updated[existing] = group;
            }
            else
            {
                if (updated.Count >= MaximumGroups)
                {
                    throw new InvalidOperationException(
                        $"Maksimal {MaximumGroups} grup kelas.");
                }

                updated.Add(group);
            }

            updated = updated
                .OrderBy(
                    candidate => candidate.Name,
                    StringComparer.OrdinalIgnoreCase)
                .ToList();
            Save(updated);
            _groups = updated;
            return group;
        }
    }

    public bool Delete(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        lock (_sync)
        {
            var updated = _groups
                .Where(group => !string.Equals(
                    group.Name,
                    name.Trim(),
                    StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (updated.Count == _groups.Count) return false;

            Save(updated);
            _groups = updated;
            return true;
        }
    }
    private List<ClassroomGroupDefinition> Load()
    {
        if (!File.Exists(_storagePath)) return new List<ClassroomGroupDefinition>();

        try
        {
            var json = File.ReadAllText(_storagePath);
            var stored = JsonSerializer.Deserialize<List<StoredGroup>>(json)
                         ?? new List<StoredGroup>();
            var groups = new List<ClassroomGroupDefinition>();
            foreach (var item in stored.Take(MaximumGroups))
            {
                try
                {
                    groups.Add(Normalize(
                        item.Name ?? string.Empty,
                        item.PcNames ?? Array.Empty<string>()));
                }
                catch (ArgumentException)
                {
                    // Abaikan entry rusak, lanjutkan membaca grup lain.
                }
            }

            return groups
                .GroupBy(
                    group => group.Name,
                    StringComparer.OrdinalIgnoreCase)
                .Select(group => group.Last())
                .OrderBy(
                    group => group.Name,
                    StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex) when (
            ex is IOException
                or UnauthorizedAccessException
                or JsonException)
        {
            _logger?.LogWarning(
                ex,
                "Daftar grup kelas gagal dibaca dari {Path}",
                _storagePath);
            return new List<ClassroomGroupDefinition>();
        }
    }

    private void Save(IReadOnlyCollection<ClassroomGroupDefinition> groups)
    {
        var directory = Path.GetDirectoryName(_storagePath)
                        ?? throw new InvalidOperationException(
                            "Folder grup kelas tidak valid.");
        Directory.CreateDirectory(directory);

        var payload = groups.Select(group => new StoredGroup
        {
            Name = group.Name,
            PcNames = group.PcNames.ToArray(),
        });
        var json = JsonSerializer.Serialize(
            payload,
            new JsonSerializerOptions { WriteIndented = true });
        var temporaryPath = _storagePath + ".tmp";
        File.WriteAllText(temporaryPath, json);
        File.Move(temporaryPath, _storagePath, overwrite: true);
    }

    private static ClassroomGroupDefinition Normalize(
        string name,
        IEnumerable<string> pcNames)
    {
        ArgumentNullException.ThrowIfNull(pcNames);
        var normalizedName = name?.Trim() ?? string.Empty;
        if (normalizedName.Length is < 1 or > MaximumGroupNameLength
            || normalizedName.Any(char.IsControl))
        {
            throw new ArgumentException(
                $"Nama grup wajib 1-{MaximumGroupNameLength} karakter.",
                nameof(name));
        }

        var members = pcNames
            .Select(pcName => pcName?.Trim() ?? string.Empty)
            .Where(pcName => pcName.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(pcName => pcName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (members.Length is < 1 or > MaximumMembersPerGroup
            || members.Any(pcName => !HubSecurity.IsValidPcName(pcName)))
        {
            throw new ArgumentException(
                $"Anggota grup wajib 1-{MaximumMembersPerGroup} nama PC valid.",
                nameof(pcNames));
        }

        return new ClassroomGroupDefinition(normalizedName, members);
    }

    private static string ResolveStoragePath(IConfiguration configuration)
    {
        var configured = configuration["Teacher:ClassroomGroupsPath"];
        if (!string.IsNullOrWhiteSpace(configured)) return configured;

        return Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "LabKom",
            "classroom-groups.json");
    }

    private sealed class StoredGroup
    {
        public string? Name { get; set; }
        public string[]? PcNames { get; set; }
    }
}