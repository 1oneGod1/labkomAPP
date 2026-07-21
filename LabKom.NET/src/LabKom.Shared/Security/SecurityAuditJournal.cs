using System.Security.AccessControl;
using System.Security.Principal;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LabKom.Shared.Security;

/// <summary>
/// Append-only, HMAC chained security journal. A protected anchor detects
/// modification, reordering, and truncation between application runs.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class SecurityAuditJournal
{
    private static readonly byte[] AnchorEntropy = "LabKom.AuditAnchor.v1"u8.ToArray();
    private readonly object _sync = new();
    private readonly string _journalPath;
    private readonly string _anchorPath;
    private readonly byte[] _key;
    private readonly bool _protectAnchor;
    private readonly DataProtectionScope _anchorScope;
    private readonly bool _restrictToAdministrators;
    private long _sequence;
    private string _lastHash = new('0', 64);

    public static string DefaultMachinePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "LabKom",
        "Security",
        "AdminAudit",
        "security-admin-audit.jsonl");

    public bool IntegrityValid { get; private set; }
    public long LastSequence => _sequence;

    public SecurityAuditJournal(
        string rootSecret,
        string journalPath,
        string? anchorPath = null,
        bool protectAnchor = true,
        DataProtectionScope anchorScope = DataProtectionScope.CurrentUser,
        bool restrictToAdministrators = false)
    {
        if (string.IsNullOrWhiteSpace(rootSecret) || rootSecret.Length < 32)
            throw new ArgumentException("Audit root secret terlalu pendek.", nameof(rootSecret));
        _journalPath = Path.GetFullPath(journalPath);
        _anchorPath = Path.GetFullPath(anchorPath ?? journalPath + ".anchor");
        _protectAnchor = protectAnchor;
        _anchorScope = anchorScope;
        _restrictToAdministrators = restrictToAdministrators;
        _key = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(rootSecret),
            "LabKom.SecurityAudit.v1"u8.ToArray());
        if (_restrictToAdministrators)
        {
            EnsureJournalDirectory();
            ApplyExistingMachineAcls();
        }
        IntegrityValid = VerifyExisting();
    }

    public static SecurityAuditJournal OpenMachineJournal(string rootSecret) =>
        new(
            rootSecret,
            DefaultMachinePath,
            protectAnchor: true,
            anchorScope: DataProtectionScope.LocalMachine,
            restrictToAdministrators: true);

    public SecurityAuditRecord Append(
        string actorSid,
        string actorName,
        LabKomRole role,
        string action,
        string? target,
        string outcome,
        string? detail = null)
    {
        lock (_sync)
        {
            IntegrityValid = VerifyExisting();
            if (!IntegrityValid)
                throw new InvalidDataException(
                    "Integritas security audit gagal; operasi sensitif dihentikan.");

            var unsigned = new SecurityAuditRecord(
                Sequence: _sequence + 1,
                TimestampUtc: DateTimeOffset.UtcNow,
                ActorSid: Limit(actorSid, 184),
                ActorName: Limit(actorName, 256),
                Role: role,
                Action: Limit(action, 128),
                Target: LimitNullable(target, 256),
                Outcome: Limit(outcome, 64),
                Detail: LimitNullable(detail, 1024),
                PreviousHash: _lastHash,
                EntryHash: string.Empty);
            var record = unsigned with { EntryHash = ComputeHash(unsigned) };

            EnsureJournalDirectory();
            if (_restrictToAdministrators && !File.Exists(_journalPath))
            {
                using (File.Create(_journalPath))
                {
                }
                ApplyAdministratorFileAcl(_journalPath);
            }
            using (var stream = new FileStream(
                       _journalPath,
                       FileMode.Append,
                       FileAccess.Write,
                       FileShare.Read,
                       4096,
                       FileOptions.WriteThrough))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                writer.WriteLine(JsonSerializer.Serialize(record, JsonOptions));
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            _sequence = record.Sequence;
            _lastHash = record.EntryHash;
            WriteAnchor(new SecurityAuditAnchor(_sequence, _lastHash));
            return record;
        }
    }

    public bool VerifyExisting()
    {
        lock (_sync)
        {
            try
            {
                var sequence = 0L;
                var previous = new string('0', 64);
                if (File.Exists(_journalPath))
                {
                    foreach (var line in File.ReadLines(_journalPath))
                    {
                        if (string.IsNullOrWhiteSpace(line)) return false;
                        var record = JsonSerializer.Deserialize<SecurityAuditRecord>(line, JsonOptions)
                                     ?? throw new InvalidDataException("Record audit kosong.");
                        if (record.Sequence != sequence + 1
                            || !FixedEquals(record.PreviousHash, previous)
                            || !FixedEquals(record.EntryHash, ComputeHash(record with { EntryHash = string.Empty })))
                            return false;
                        sequence = record.Sequence;
                        previous = record.EntryHash;
                    }
                }

                var anchor = ReadAnchor();
                if (anchor is not null
                    && (anchor.Sequence != sequence || !FixedEquals(anchor.LastHash, previous)))
                    return false;
                if (anchor is null && sequence > 0) return false;

                _sequence = sequence;
                _lastHash = previous;
                return true;
            }
            catch (Exception exception) when (
                exception is IOException
                    or UnauthorizedAccessException
                    or JsonException
                    or CryptographicException
                    or InvalidDataException)
            {
                return false;
            }
        }
    }
    public IReadOnlyList<SecurityAuditRecord> ReadRecent(int maximum = 100)
    {
        if (maximum is < 1 or > 1000)
            throw new ArgumentOutOfRangeException(nameof(maximum));

        lock (_sync)
        {
            IntegrityValid = VerifyExisting();
            if (!IntegrityValid)
                throw new InvalidDataException("Integritas security audit gagal.");

            var records = new Queue<SecurityAuditRecord>(maximum);
            if (!File.Exists(_journalPath)) return Array.Empty<SecurityAuditRecord>();
            foreach (var line in File.ReadLines(_journalPath))
            {
                var record = JsonSerializer.Deserialize<SecurityAuditRecord>(
                    line,
                    JsonOptions)
                    ?? throw new InvalidDataException("Record audit kosong.");
                if (records.Count == maximum) records.Dequeue();
                records.Enqueue(record);
            }
            return records.ToArray();
        }
    }


    private string ComputeHash(SecurityAuditRecord record)
    {
        var canonical = string.Join('\n', new[]
        {
            record.Sequence.ToString(System.Globalization.CultureInfo.InvariantCulture),
            record.TimestampUtc.ToUniversalTime().ToString("O"),
            Encode(record.ActorSid),
            Encode(record.ActorName),
            ((int)record.Role).ToString(System.Globalization.CultureInfo.InvariantCulture),
            Encode(record.Action),
            Encode(record.Target),
            Encode(record.Outcome),
            Encode(record.Detail),
            record.PreviousHash,
        });
        return Convert.ToHexString(HMACSHA256.HashData(
            _key,
            Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    private void WriteAnchor(SecurityAuditAnchor anchor)
    {
        var clear = JsonSerializer.SerializeToUtf8Bytes(anchor, JsonOptions);
        var stored = _protectAnchor
            ? ProtectedData.Protect(clear, AnchorEntropy, _anchorScope)
            : clear;
        var temporary = _anchorPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            EnsureJournalDirectory();
            File.WriteAllBytes(temporary, stored);
            if (_restrictToAdministrators)
                ApplyAdministratorFileAcl(temporary);
            File.Move(temporary, _anchorPath, overwrite: true);
            if (_restrictToAdministrators)
                ApplyAdministratorFileAcl(_anchorPath);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(clear);
            if (!ReferenceEquals(stored, clear)) CryptographicOperations.ZeroMemory(stored);
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private SecurityAuditAnchor? ReadAnchor()
    {
        if (!File.Exists(_anchorPath)) return null;
        var stored = File.ReadAllBytes(_anchorPath);
        byte[]? clear = null;
        try
        {
            clear = _protectAnchor
                ? ProtectedData.Unprotect(stored, AnchorEntropy, _anchorScope)
                : stored;
            return JsonSerializer.Deserialize<SecurityAuditAnchor>(clear, JsonOptions)
                   ?? throw new InvalidDataException("Anchor audit kosong.");
        }
        finally
        {
            if (!ReferenceEquals(clear, stored)) CryptographicOperations.ZeroMemory(stored);
            if (clear is not null) CryptographicOperations.ZeroMemory(clear);
        }
    }

    private void EnsureJournalDirectory()
    {
        foreach (var directory in new[]
                 {
                     Path.GetDirectoryName(_journalPath)!,
                     Path.GetDirectoryName(_anchorPath)!,
                 }.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            Directory.CreateDirectory(directory);
            if (_restrictToAdministrators)
                ApplyAdministratorDirectoryAcl(directory);
        }
    }

    private void ApplyExistingMachineAcls()
    {
        if (File.Exists(_journalPath))
            ApplyAdministratorFileAcl(_journalPath);
        if (File.Exists(_anchorPath))
            ApplyAdministratorFileAcl(_anchorPath);
    }

    private static void ApplyAdministratorFileAcl(string path)
    {
        var security = new FileSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            FileSystemRights.FullControl,
            AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            FileSystemRights.FullControl,
            AccessControlType.Allow));
        new FileInfo(path).SetAccessControl(security);
    }

    private static void ApplyAdministratorDirectoryAcl(string path)
    {
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        var inheritance = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            FileSystemRights.FullControl,
            inheritance,
            PropagationFlags.None,
            AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            FileSystemRights.FullControl,
            inheritance,
            PropagationFlags.None,
            AccessControlType.Allow));
        new DirectoryInfo(path).SetAccessControl(security);
    }

    private static bool FixedEquals(string left, string right)
    {
        var leftBytes = Encoding.ASCII.GetBytes(left);
        var rightBytes = Encoding.ASCII.GetBytes(right);
        return leftBytes.Length == rightBytes.Length
               && CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }

    private static string Encode(string? value) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? string.Empty));

    private static string Limit(string value, int maximum) =>
        value.Length <= maximum ? value : value[..maximum];

    private static string? LimitNullable(string? value, int maximum) =>
        value is null ? null : Limit(value, maximum);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };
}

public sealed record SecurityAuditRecord(
    long Sequence,
    DateTimeOffset TimestampUtc,
    string ActorSid,
    string ActorName,
    LabKomRole Role,
    string Action,
    string? Target,
    string Outcome,
    string? Detail,
    string PreviousHash,
    string EntryHash);

public sealed record SecurityAuditAnchor(long Sequence, string LastHash);
