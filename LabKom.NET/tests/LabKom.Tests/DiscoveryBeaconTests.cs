using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using LabKom.Shared.Discovery;
using LabKom.Shared.Hub;
using LabKom.Shared.Security;
using LabKom.Teacher.Services;

namespace LabKom.Tests;

public sealed class DiscoveryBeaconTests
{
    private static readonly string Secret = new('s', HubSecurity.MinimumSecretLength);
    private static readonly string PublishedPin = new('a', 64);

    [Fact]
    public void SignedBeaconIsAccepted()
    {
        var beacon = DiscoveryBeacon.CreateSigned("teacher-01", "Lab A", "10.10.10.1", 41235, PublishedPin, Secret);

        Assert.True(beacon.IsAuthentic(Secret));
        Assert.True(beacon.IsStructurallyValid());
    }

    [Fact]
    public void TamperedOrWrongKeyBeaconIsRejected()
    {
        var beacon = DiscoveryBeacon.CreateSigned("teacher-01", "Lab A", "10.10.10.1", 41235, PublishedPin, Secret);

        Assert.False((beacon with { Ip = "10.10.10.99" }).IsAuthentic(Secret));
        Assert.False((beacon with { CertificateSha256 = new string('b', 64) }).IsAuthentic(Secret));
        Assert.False(beacon.IsAuthentic(new string('x', HubSecurity.MinimumSecretLength)));
    }

    [Fact]
    public void StaleBeaconIsRejected()
    {
        var beacon = DiscoveryBeacon.CreateSigned("teacher-01", "Lab A", "10.10.10.1", 41235, PublishedPin, Secret);
        var staleNow = beacon.TimestampUnixMs + (DiscoveryProtocol.MaximumClockSkewSeconds + 1) * 1_000L;

        Assert.False(beacon.IsAuthentic(Secret, staleNow));
    }

    [Fact]
    public void EndpointSnapshotKeepsUrlAndCertificatePinFromSameBeacon()
    {
        var store = new TeacherEndpointStore();
        var beacon = DiscoveryBeacon.CreateSigned(
            "teacher-01",
            "Lab A",
            "10.10.10.1",
            41235,
            PublishedPin,
            Secret);

        store.Update(beacon);

        var snapshot = Assert.IsType<TeacherEndpointSnapshot>(store.GetFreshSnapshot());
        Assert.Same(beacon, snapshot.Beacon);
        Assert.Equal("https://10.10.10.1:41235/hubs/teacher", snapshot.HubUrl);
    }

    [Fact]
    public void TeacherCertificateHasPrivateKeyAndMatchesPublishedPin()
    {
        using var provider = new TeacherCertificateProvider();

        Assert.True(provider.Certificate.HasPrivateKey);
        Assert.True(CertificatePin.IsValid(provider.Sha256Pin));
        Assert.True(CertificatePin.Matches(provider.Certificate, provider.Sha256Pin));
        Assert.False(CertificatePin.Matches(provider.Certificate, new string('f', 64)));
    }

    [Fact]
    public async Task TeacherCertificateCompletesWindowsTlsHandshake()
    {
        using var provider = new TeacherCertificateProvider();
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var cancellationToken = TestContext.Current.CancellationToken;
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var server = Task.Run(async () =>
            {
                using var accepted = await listener.AcceptTcpClientAsync(cancellationToken);
                await using var serverTls = new SslStream(
                    accepted.GetStream(),
                    leaveInnerStreamOpen: false);
                await serverTls.AuthenticateAsServerAsync(
                    provider.Certificate,
                    clientCertificateRequired: false,
                    checkCertificateRevocation: false);
            }, cancellationToken);

            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port, cancellationToken);
            await using var clientTls = new SslStream(
                client.GetStream(),
                leaveInnerStreamOpen: false,
                (_, _, _, _) => true);
            await clientTls.AuthenticateAsClientAsync("localhost");
            await server.WaitAsync(
                TimeSpan.FromSeconds(10),
                cancellationToken);
            Assert.True(clientTls.IsAuthenticated);
        }
        finally
        {
            listener.Stop();
        }
    }
}