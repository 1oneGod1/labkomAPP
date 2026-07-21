namespace LabKom.Shared.Contracts;

public enum WebFilterMode
{
    Disabled = 0,
    Blacklist = 1,
    Whitelist = 2,
}

/// <summary>
/// Policy domain untuk Student Agent. Implementasi saat ini mendukung blacklist
/// melalui bagian terkelola pada hosts file Windows.
/// </summary>
public sealed record WebFilterPolicy(
    string CommandId,
    WebFilterMode Mode,
    IReadOnlyList<string> Domains,
    long IssuedAtUnixMs,
    long ExpiresAtUnixMs)
{
    public static WebFilterPolicy Disabled => Create(
        WebFilterMode.Disabled,
        Array.Empty<string>());

    public static WebFilterPolicy Blacklist(IEnumerable<string> domains) => Create(
        WebFilterMode.Blacklist,
        NormalizeDomains(domains));

    public static WebFilterPolicy Whitelist(IEnumerable<string> domains) => Create(
        WebFilterMode.Whitelist,
        NormalizeDomains(domains));

    private static IReadOnlyList<string> NormalizeDomains(IEnumerable<string> domains) =>
        domains.Where(domain => !string.IsNullOrWhiteSpace(domain))
            .Select(NormalizeDomain)
            .Where(domain => domain.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string NormalizeDomain(string raw)
    {
        var value = raw.Trim();
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && uri.HostNameType == UriHostNameType.Dns)
        {
            return uri.IdnHost.Trim('.').ToLowerInvariant();
        }

        return value.Trim('.').ToLowerInvariant();
    }

    private static WebFilterPolicy Create(
        WebFilterMode mode,
        IReadOnlyList<string> domains)
    {
        var issued = DateTimeOffset.UtcNow;
        return new WebFilterPolicy(
            Guid.NewGuid().ToString("N"),
            mode,
            domains,
            issued.ToUnixTimeMilliseconds(),
            issued.AddSeconds(30).ToUnixTimeMilliseconds());
    }
}