using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LabKom.Shared.Security;
using LabKom.Shared.Updates;

namespace LabKom.Tests;

public sealed class DeploymentSecurityTests
{
    [Fact]
    public void SignedManifest_VerifiesRsaPssSignature()
    {
        using var rsa = RSA.Create(3072);
        var unsigned = CreateManifest();
        var signature = rsa.SignData(
            Encoding.UTF8.GetBytes(unsigned.CanonicalPayload),
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pss);
        var manifest = unsigned with { Signature = Convert.ToBase64String(signature) };

        manifest.Validate("Student");
        Assert.True(manifest.VerifySignature(rsa.ExportSubjectPublicKeyInfoPem()));
    }

    [Fact]
    public void SignedManifest_RejectsTamperedPackageHash()
    {
        using var rsa = RSA.Create(3072);
        var unsigned = CreateManifest();
        var signature = rsa.SignData(
            Encoding.UTF8.GetBytes(unsigned.CanonicalPayload),
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pss);
        var manifest = unsigned with
        {
            Signature = Convert.ToBase64String(signature),
            Sha256 = new string('b', 64),
        };

        Assert.False(manifest.VerifySignature(rsa.ExportSubjectPublicKeyInfoPem()));
    }

    [Fact]
    public void SignedManifest_RequiresHttps()
    {
        var manifest = CreateManifest() with
        {
            PackageUrl = "http://updates.example.invalid/LabKom.zip",
        };

        Assert.Throws<InvalidDataException>(() => manifest.Validate("Student"));
    }

    [Fact]
    public void ProvisioningBundle_RoundTripsWithoutChangingSecret()
    {
        var directory = Path.Combine(Path.GetTempPath(), "labkom-test-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "classroom.provision.json");
        try
        {
            var original = ProvisionedSecretStore.Create("Lab A");
            var json = JsonSerializer.Serialize(original, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            });
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, json);
            var imported = ProvisionedSecretStore.ImportProvisioningBundle(path);

            Assert.Equal(original, imported);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    private static SignedUpdateManifest CreateManifest() => new()
    {
        SchemaVersion = SignedUpdateManifest.CurrentSchemaVersion,
        Component = "Student",
        Version = "1.2.3",
        PublishedAtUtc = new DateTimeOffset(2026, 7, 17, 0, 0, 0, TimeSpan.Zero),
        PackageUrl = "https://updates.example.invalid/LabKom-Student-1.2.3.zip",
        Sha256 = new string('a', 64),
        Signature = Convert.ToBase64String([1, 2, 3]),
    };
}
