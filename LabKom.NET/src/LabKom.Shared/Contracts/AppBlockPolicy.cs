namespace LabKom.Shared.Contracts;

/// <summary>
/// Daftar nama executable (tanpa .exe) yang akan dibunuh oleh
/// Student Agent saat terdeteksi running.
/// </summary>
public sealed record AppBlockPolicy(
    bool Enabled,
    IReadOnlyList<string> ProcessNames,
    long TimestampUnixMs)
{
    public static AppBlockPolicy Disabled => new(
        false,
        Array.Empty<string>(),
        DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

    public static AppBlockPolicy Block(IEnumerable<string> names) => new(
        true,
        names.Where(n => !string.IsNullOrWhiteSpace(n))
             .Select(NormalizeName)
             .Distinct(StringComparer.OrdinalIgnoreCase)
             .ToArray(),
        DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

    private static string NormalizeName(string raw)
    {
        var n = raw.Trim();
        if (n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            n = n[..^4];
        return n;
    }
}
