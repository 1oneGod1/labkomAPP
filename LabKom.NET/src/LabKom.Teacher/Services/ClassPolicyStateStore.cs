using LabKom.Shared.Contracts;

namespace LabKom.Teacher.Services;

/// <summary>
/// Desired classroom policies retained for the running Teacher session.
/// Replays create fresh command identities so reconnecting Agents can validate
/// TTL and return an acknowledgement.
/// </summary>
public sealed class ClassPolicyStateStore
{
    private readonly object _sync = new();
    private WebFilterMode _webMode = WebFilterMode.Disabled;
    private string[] _domains = Array.Empty<string>();
    private bool _appBlockEnabled;
    private string[] _processNames = Array.Empty<string>();

    public void Apply(WebFilterPolicy policy)
    {
        lock (_sync)
        {
            _webMode = policy.Mode;
            _domains = policy.Domains.ToArray();
        }
    }

    public void Apply(AppBlockPolicy policy)
    {
        lock (_sync)
        {
            _appBlockEnabled = policy.Enabled;
            _processNames = policy.ProcessNames.ToArray();
        }
    }

    public WebFilterPolicy BuildWebReplay()
    {
        lock (_sync)
        {
            return _webMode == WebFilterMode.Blacklist
                ? WebFilterPolicy.Blacklist(_domains)
                : WebFilterPolicy.Disabled;
        }
    }

    public AppBlockPolicy BuildAppReplay()
    {
        lock (_sync)
        {
            return _appBlockEnabled
                ? AppBlockPolicy.Block(_processNames)
                : AppBlockPolicy.Disabled;
        }
    }
}