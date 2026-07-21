using System.Security.Cryptography;
using System.Text;

namespace LabKom.Shared.Hub;

/// <summary>Validasi identitas dan secret untuk control plane LabKom.</summary>
public static class HubSecurity
{
    public const string HeaderName = "X-LabKom-Key";
    public const string DeviceIdHeaderName = "X-LabKom-Device-Id";
    public const string KeyVersionHeaderName = "X-LabKom-Key-Version";
    public const string PcNameHeaderName = "X-LabKom-Pc";
    public const int MinimumSecretLength = 32;
    public const int MaximumPcNameLength = 63;

    public static bool IsStrongSecret(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length >= MinimumSecretLength;

    public static bool IsValidSecret(string? expected, string? supplied)
    {
        if (!IsStrongSecret(expected) || string.IsNullOrWhiteSpace(supplied)) return false;
        var left = Encoding.UTF8.GetBytes(expected!);
        var right = Encoding.UTF8.GetBytes(supplied);
        return left.Length == right.Length && CryptographicOperations.FixedTimeEquals(left, right);
    }

    public static bool IsValidPcName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaximumPcNameLength) return false;
        foreach (var character in value)
        {
            if (char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.') continue;
            return false;
        }
        return true;
    }
}