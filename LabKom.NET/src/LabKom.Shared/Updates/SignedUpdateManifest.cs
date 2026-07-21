using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace LabKom.Shared.Updates;

public sealed record SignedUpdateManifest
{
    public const int CurrentSchemaVersion = 1;

    public required int SchemaVersion { get; init; }
    public required string Component { get; init; }
    public required string Version { get; init; }
    public required DateTimeOffset PublishedAtUtc { get; init; }
    public required string PackageUrl { get; init; }
    public required string Sha256 { get; init; }
    public required string Signature { get; init; }

    [JsonIgnore]
    public string CanonicalPayload => string.Join(
        '\n',
        SchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
        Component.Trim(),
        Version.Trim(),
        PublishedAtUtc.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture),
        PackageUrl.Trim(),
        Sha256.Trim().ToLowerInvariant());

    public void Validate(string expectedComponent, bool requireHttps = true)
    {
        if (SchemaVersion != CurrentSchemaVersion)
            throw new InvalidDataException($"Schema update {SchemaVersion} tidak didukung.");
        if (!string.Equals(Component, expectedComponent, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Komponen pada manifest update tidak sesuai.");
        if (!System.Version.TryParse(Version, out _))
            throw new InvalidDataException("Versi pada manifest update tidak valid.");
        if (!Uri.TryCreate(PackageUrl, UriKind.Absolute, out var packageUri))
            throw new InvalidDataException("URL paket update tidak valid.");
        if (requireHttps && packageUri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidDataException("Paket update wajib menggunakan HTTPS.");
        if (Sha256.Length != 64 || !Sha256.All(Uri.IsHexDigit))
            throw new InvalidDataException("SHA-256 paket update tidak valid.");
        try
        {
            _ = Convert.FromBase64String(Signature);
        }
        catch (FormatException exception)
        {
            throw new InvalidDataException("Tanda tangan manifest bukan Base64 yang valid.", exception);
        }
    }

    public bool VerifySignature(string publicKeyPem)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publicKeyPem);
        using var rsa = RSA.Create();
        rsa.ImportFromPem(publicKeyPem);
        return VerifySignature(rsa);
    }

    public bool VerifySignature(RSA publicKey)
    {
        ArgumentNullException.ThrowIfNull(publicKey);
        return publicKey.VerifyData(
            Encoding.UTF8.GetBytes(CanonicalPayload),
            Convert.FromBase64String(Signature),
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pss);
    }
}
