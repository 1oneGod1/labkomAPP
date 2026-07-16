using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using LabKom.Shared.Hub;
using LabKom.Shared.Security;

namespace LabKom.Shared.Discovery;

/// <summary>Authenticated UDP discovery protocol for a single classroom LAN.</summary>
public static class DiscoveryProtocol
{
    public const int Port = 41234;
    public const string Magic = "LABKOM";
    public const int Version = 2;
    public const int BroadcastIntervalSeconds = 2;
    public const int StaleAfterSeconds = 10;
    public const int MaximumClockSkewSeconds = 30;
}

/// <summary>
/// Signed discovery beacon. The signature binds the Teacher endpoint and its
/// ephemeral TLS certificate pin to the classroom shared secret.
/// </summary>
public sealed record DiscoveryBeacon(
    string Magic,
    int Version,
    string TeacherId,
    string TeacherName,
    string Ip,
    int HubPort,
    string CertificateSha256,
    long TimestampUnixMs,
    string Signature)
{
    public static DiscoveryBeacon CreateSigned(
        string teacherId,
        string teacherName,
        string ip,
        int hubPort,
        string certificateSha256,
        string sharedSecret)
    {
        if (!HubSecurity.IsStrongSecret(sharedSecret))
        {
            throw new ArgumentException("Shared secret discovery terlalu pendek.", nameof(sharedSecret));
        }
        if (!CertificatePin.IsValid(certificateSha256))
        {
            throw new ArgumentException("Pin sertifikat Teacher tidak valid.", nameof(certificateSha256));
        }

        var beacon = new DiscoveryBeacon(
            DiscoveryProtocol.Magic,
            DiscoveryProtocol.Version,
            teacherId,
            teacherName,
            ip,
            hubPort,
            certificateSha256,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            string.Empty);
        return beacon with { Signature = ComputeSignature(beacon, sharedSecret) };
    }

    public bool IsAuthentic(string sharedSecret, long? nowUnixMs = null)
    {
        if (!IsStructurallyValid() || !HubSecurity.IsStrongSecret(sharedSecret)) return false;

        var now = nowUnixMs ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var maximumSkew = DiscoveryProtocol.MaximumClockSkewSeconds * 1_000L;
        if (TimestampUnixMs > now + maximumSkew || TimestampUnixMs < now - maximumSkew) return false;
        if (string.IsNullOrWhiteSpace(Signature) || Signature.Length != 64 || Signature.Any(character => !Uri.IsHexDigit(character))) return false;

        var expected = Convert.FromHexString(ComputeSignature(this, sharedSecret));
        var supplied = Convert.FromHexString(Signature);
        return CryptographicOperations.FixedTimeEquals(expected, supplied);
    }

    public bool IsStructurallyValid() =>
        Magic == DiscoveryProtocol.Magic
        && Version == DiscoveryProtocol.Version
        && !string.IsNullOrWhiteSpace(TeacherId) && TeacherId.Length <= 128
        && !string.IsNullOrWhiteSpace(TeacherName) && TeacherName.Length <= 128
        && !string.IsNullOrWhiteSpace(Ip) && IPAddress.TryParse(Ip, out var address)
        && address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
        && HubPort is > 0 and <= 65_535
        && CertificatePin.IsValid(CertificateSha256);

    private static string ComputeSignature(DiscoveryBeacon beacon, string sharedSecret)
    {
        var canonical = string.Join('\n',
            Encode(beacon.Magic),
            beacon.Version.ToString(CultureInfo.InvariantCulture),
            Encode(beacon.TeacherId),
            Encode(beacon.TeacherName),
            Encode(beacon.Ip),
            beacon.HubPort.ToString(CultureInfo.InvariantCulture),
            Encode(beacon.CertificateSha256),
            beacon.TimestampUnixMs.ToString(CultureInfo.InvariantCulture));

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(sharedSecret));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    private static string Encode(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
}