using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace LabKom.Shared.Security;

/// <summary>Validates a server certificate against a SHA-256 discovery pin.</summary>
public static class CertificatePin
{
    public static bool IsValid(string? value) =>
        value is { Length: 64 } && value.All(Uri.IsHexDigit);

    public static bool Matches(X509Certificate? certificate, string? expectedSha256)
    {
        if (certificate is null || !IsValid(expectedSha256)) return false;

        var actual = SHA256.HashData(certificate.GetRawCertData());
        var expected = Convert.FromHexString(expectedSha256!);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    public static string From(X509Certificate certificate) =>
        Convert.ToHexString(SHA256.HashData(certificate.GetRawCertData())).ToLowerInvariant();
}