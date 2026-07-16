using LabKom.Shared.Hub;

namespace LabKom.Tests;

public sealed class HubSecurityTests
{
    [Fact]
    public void SecretMustBeStrongAndMatchExactly()
    {
        var secret = new string('x', HubSecurity.MinimumSecretLength);

        Assert.True(HubSecurity.IsStrongSecret(secret));
        Assert.True(HubSecurity.IsValidSecret(secret, secret));
        Assert.False(HubSecurity.IsValidSecret(secret, secret + "x"));
        Assert.False(HubSecurity.IsValidSecret("short", "short"));
    }

    [Theory]
    [InlineData("PC-01", true)]
    [InlineData("LAB_02.domain", true)]
    [InlineData("pc:agent", false)]
    [InlineData("pc siswa", false)]
    [InlineData("", false)]
    public void PcNameValidationPreventsGroupInjection(string value, bool expected)
    {
        Assert.Equal(expected, HubSecurity.IsValidPcName(value));
    }

    [Fact]
    public void AudiencesAreSeparatedByRoleAndPc()
    {
        Assert.Equal("role:agent", HubRoutes.Groups.ForRole(HubRoutes.Roles.Agent));
        Assert.Equal("role:desktop", HubRoutes.Groups.ForRole(HubRoutes.Roles.Desktop));
        Assert.Equal("pc:pc-01:agent", HubRoutes.Groups.ForPcRole("PC-01", HubRoutes.Roles.Agent));
        Assert.Equal("pc:pc-01:desktop", HubRoutes.Groups.ForPcRole("PC-01", HubRoutes.Roles.Desktop));
        Assert.Throws<ArgumentException>(() => HubRoutes.Groups.ForPcRole("pc:escape", HubRoutes.Roles.Agent));
    }

    [Fact]
    public void ClientUrlContainsOnlyRoleAndPcIdentity()
    {
        var url = HubRoutes.BuildClientUrl(
            "https://10.10.10.1:41235/hubs/teacher",
            HubRoutes.Roles.Desktop,
            "PC-01");

        Assert.Equal("https://10.10.10.1:41235/hubs/teacher?role=desktop&pc=PC-01", url);
        Assert.DoesNotContain("secret", url, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(HubSecurity.HeaderName, url, StringComparison.OrdinalIgnoreCase);
    }
}