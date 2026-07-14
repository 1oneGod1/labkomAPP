using System.Security.Cryptography;
using System.Text;

namespace LabKom.Shared.Hub;

public static class HubSecurity
{
    public const string QueryKey = "access_token";
    public const string HeaderName = "X-LabKom-Key";

    public static bool IsValidSecret(string? expected, string? supplied)
    {
        if (string.IsNullOrWhiteSpace(expected) || string.IsNullOrWhiteSpace(supplied)) return false;
        var left = Encoding.UTF8.GetBytes(expected);
        var right = Encoding.UTF8.GetBytes(supplied);
        return left.Length == right.Length && CryptographicOperations.FixedTimeEquals(left, right);
    }

}