using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Configuration;
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
    public string? PinFilePath { get; }

    public TeacherCertificateProvider()
        : this(configuration: null)
    {
    }

    public TeacherCertificateProvider(IConfiguration? configuration)
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

        using var generated = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-5),
            DateTimeOffset.UtcNow.AddDays(7));
        var pfx = generated.Export(X509ContentType.Pfx);
        try
        {
            Certificate = new X509Certificate2(
                pfx,
                (string?)null,
                X509KeyStorageFlags.UserKeySet
                | X509KeyStorageFlags.Exportable);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(pfx);
        }
        Sha256Pin = CertificatePin.From(Certificate);
        var configuredPath = configuration?["Security:TlsPinPath"];
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            PinFilePath = PublishPin(
                Environment.ExpandEnvironmentVariables(configuredPath),
                Sha256Pin);
        }
    }
    private static string PublishPin(string configuredPath, string pin)
    {
        var path = Path.GetFullPath(configuredPath);
        var directory = Path.GetDirectoryName(path)
                        ?? throw new InvalidOperationException(
                            "Folder file pin TLS Teacher tidak valid.");
        Directory.CreateDirectory(directory);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, pin + Environment.NewLine);
            File.Move(temporary, path, overwrite: true);
            return path;
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }


    public void Dispose()
    {
        Certificate.Dispose();
        _privateKey.Dispose();
    }
}