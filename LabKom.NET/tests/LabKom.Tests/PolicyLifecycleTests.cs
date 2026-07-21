using LabKom.Shared.Contracts;
using LabKom.Teacher.Services;

namespace LabKom.Tests;

public sealed class PolicyLifecycleTests
{
    [Fact]
    public void WebBlacklistNormalizesUrlsAndRejectsUnsupportedWhitelist()
    {
        var policy = WebFilterPolicy.Blacklist(
            new[]
            {
                "https://YouTube.com/watch?v=1",
                "youtube.com.",
            });

        Assert.Single(policy.Domains);
        Assert.Equal("youtube.com", policy.Domains[0]);
        Assert.True(ContractValidation.IsValidWebFilterPolicy(policy));
        Assert.False(ContractValidation.IsValidWebFilterPolicy(
            WebFilterPolicy.Whitelist(new[] { "school.example" })));
        Assert.False(ContractValidation.IsValidWebFilterPolicy(
            policy with { Domains = new[] { "youtube.com/path" } }));
    }

    [Fact]
    public void AppBlockNormalizesExecutableNamesAndRejectsPaths()
    {
        var policy = AppBlockPolicy.Block(
            new[] { "chrome.exe", "CHROME", "steam" });

        Assert.Equal(2, policy.ProcessNames.Count);
        Assert.Contains(
            policy.ProcessNames,
            name => string.Equals(
                name,
                "chrome",
                StringComparison.OrdinalIgnoreCase));
        Assert.True(ContractValidation.IsValidAppBlockPolicy(policy));
        Assert.False(ContractValidation.IsValidAppBlockPolicy(
            policy with { ProcessNames = new[] { @"C:\Windows\notepad" } }));
    }

    [Fact]
    public void PolicyReplayUsesFreshCommandIdentityAndRetainsDesiredState()
    {
        var store = new ClassPolicyStateStore();
        var originalWeb = WebFilterPolicy.Blacklist(
            new[] { "social.example" });
        var originalApps = AppBlockPolicy.Block(
            new[] { "game" });

        store.Apply(originalWeb);
        store.Apply(originalApps);

        var webReplay = store.BuildWebReplay();
        var appReplay = store.BuildAppReplay();

        Assert.NotEqual(originalWeb.CommandId, webReplay.CommandId);
        Assert.NotEqual(originalApps.CommandId, appReplay.CommandId);
        Assert.Equal(originalWeb.Domains, webReplay.Domains);
        Assert.Equal(originalApps.ProcessNames, appReplay.ProcessNames);
        Assert.True(ContractValidation.IsValidWebFilterPolicy(webReplay));
        Assert.True(ContractValidation.IsValidAppBlockPolicy(appReplay));
    }

    [Fact]
    public void DefaultReplayExplicitlyDisablesStaleAgentPolicies()
    {
        var store = new ClassPolicyStateStore();

        var web = store.BuildWebReplay();
        var apps = store.BuildAppReplay();

        Assert.Equal(WebFilterMode.Disabled, web.Mode);
        Assert.Empty(web.Domains);
        Assert.False(apps.Enabled);
        Assert.Empty(apps.ProcessNames);
        Assert.True(ContractValidation.IsValidWebFilterPolicy(web));
        Assert.True(ContractValidation.IsValidAppBlockPolicy(apps));
    }
}