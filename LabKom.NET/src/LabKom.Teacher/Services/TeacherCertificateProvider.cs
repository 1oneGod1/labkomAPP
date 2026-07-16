using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using LabKom.Shared.Security;

namespace LabKom.Teacher.Services;

/// <summary>
/// Creates an in-memory TLS certificate for the current Teacher process.
/// Its SHA-256 pin is distributed inside the HMAC-signed discovery beacon.
/// </summary>
public sealed class TeacherCertificateProvider : IDisposable
{
    private readonly RSA _privateKey;

    public X509Certificate2 Certificate { get; }
    public string Sha256Pin { get; }

    public TeacherCertificateProvider()
    {
        _privateKey = RSA.Create(3072);
        var request = new CertificateRequest(
            $"CN=LabKom-{Environment.MachineName}",
            _privateKey,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection { new("1.3.6.1.5.5.7.3.1") },
            true));

        var san = new SubjectAlternativeNameBuilder();

        san.AddDnsName("localhost");
        san.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(san.Build());

        Certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-5),
            DateTimeOffset.UtcNow.AddDays(7));
        Sha256Pin = CertificatePin.From(Certificate);
    }

    public void Dispose()
    {
        Certificate.Dispose();
        _privateKey.Dispose();
    }
}