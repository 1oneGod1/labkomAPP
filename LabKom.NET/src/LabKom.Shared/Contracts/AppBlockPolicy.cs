namespace LabKom.Shared.Contracts;

/// <summary>
/// Daftar nama executable (tanpa .exe) yang akan dihentikan Student Agent.
/// Policy memiliki command identity dan TTL untuk acknowledgement yang aman.
/// </summary>
public sealed record AppBlockPolicy(
    string CommandId,
    bool Enabled,
    IReadOnlyList<string> ProcessNames,
    long IssuedAtUnixMs,
    long ExpiresAtUnixMs)
{
    public static AppBlockPolicy Disabled => Create(false, Array.Empty<string>());

    public static AppBlockPolicy Block(IEnumerable<string> names) =>
        Create(
            true,
            names.Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(NormalizeName)
                .Where(name => name.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray());

    private static AppBlockPolicy Create(bool enabled, IReadOnlyList<string> names)
    {
        var issued = DateTimeOffset.UtcNow;
        return new AppBlockPolicy(
            Guid.NewGuid().ToString("N"),
            enabled,
            names,
            issued.ToUnixTimeMilliseconds(),
            issued.AddSeconds(30).ToUnixTimeMilliseconds());
    }

    private static string NormalizeName(string raw)
    {
        var name = raw.Trim();
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^4];
        }

        return name;
    }
}