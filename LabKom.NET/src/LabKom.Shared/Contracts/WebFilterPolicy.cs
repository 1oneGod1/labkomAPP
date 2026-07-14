namespace LabKom.Shared.Contracts;

public enum WebFilterMode
{
    Disabled = 0,
    Blacklist = 1,    // Blokir domain dalam Domains
    Whitelist = 2,    // Hanya izinkan domain dalam Domains, blokir sisanya
}

/// <summary>
/// Policy yang dipush guru ke semua siswa. Diterapkan via hosts file
/// (127.0.0.1 entry untuk domain yang diblokir).
/// </summary>
public sealed record WebFilterPolicy(
    WebFilterMode Mode,
    IReadOnlyList<string> Domains,
    long TimestampUnixMs)
{
    public static WebFilterPolicy Disabled => new(
        WebFilterMode.Disabled,
        Array.Empty<string>(),
        DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

    public static WebFilterPolicy Blacklist(IEnumerable<string> domains) => new(
        WebFilterMode.Blacklist,
        domains.Where(d => !string.IsNullOrWhiteSpace(d))
               .Select(d => d.Trim().ToLowerInvariant())
               .Distinct()
               .ToArray(),
        DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

    public static WebFilterPolicy Whitelist(IEnumerable<string> domains) => new(
        WebFilterMode.Whitelist,
        domains.Where(d => !string.IsNullOrWhiteSpace(d))
               .Select(d => d.Trim().ToLowerInvariant())
               .Distinct()
               .ToArray(),
        DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
}
