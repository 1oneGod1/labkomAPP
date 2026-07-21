using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using LabKom.Shared.Hub;

namespace LabKom.Shared.Contracts;

public sealed record DeviceKeyRotationNotice(
    string DeviceId,
    int CurrentKeyVersion,
    long IssuedAtUnixMs,
    string Challenge);

public sealed record DeviceKeyRotationReceipt(
    string DeviceId,
    int KeyVersion,
    long AppliedAtUnixMs,
    string Challenge,
    string Proof);

public static class DeviceKeyRotationProtocol
{
    private const long MaximumClockSkewMs = 5 * 60 * 1000L;

    public static DeviceKeyRotationNotice CreateNotice(
        string deviceId,
        int currentKeyVersion)
    {
        if (!Guid.TryParseExact(deviceId, "N", out _)
            || currentKeyVersion is < 1 or > 100_000)
            throw new ArgumentException("Identitas atau versi rotasi tidak valid.");

        return new DeviceKeyRotationNotice(
            deviceId,
            currentKeyVersion,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
    }

    public static bool IsValidNotice(
        DeviceKeyRotationNotice notice,
        string expectedDeviceId,
        int currentDeviceVersion)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return Guid.TryParseExact(notice.DeviceId, "N", out _)
               && string.Equals(
                   notice.DeviceId,
                   expectedDeviceId,
                   StringComparison.Ordinal)
               && notice.CurrentKeyVersion is >= 1 and <= 100_000
               && notice.CurrentKeyVersion >= currentDeviceVersion
               && Math.Abs(now - notice.IssuedAtUnixMs) <= MaximumClockSkewMs
               && IsChallengeValid(notice.Challenge);
    }

    public static DeviceKeyRotationReceipt CreateReceipt(
        DeviceKeyRotationNotice notice,
        string newDeviceSecret,
        string pcName)
    {
        if (!HubSecurity.IsStrongSecret(newDeviceSecret)
            || !HubSecurity.IsValidPcName(pcName))
            throw new ArgumentException("Material receipt rotasi tidak valid.");

        var receipt = new DeviceKeyRotationReceipt(
            notice.DeviceId,
            notice.CurrentKeyVersion,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            notice.Challenge,
            string.Empty);
        return receipt with
        {
            Proof = ComputeProof(newDeviceSecret, notice, receipt, pcName),
        };
    }

    public static bool ValidateReceipt(
        DeviceKeyRotationNotice notice,
        DeviceKeyRotationReceipt receipt,
        string expectedNewDeviceSecret,
        string pcName)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (!HubSecurity.IsStrongSecret(expectedNewDeviceSecret)
            || !HubSecurity.IsValidPcName(pcName)
            || !string.Equals(receipt.DeviceId, notice.DeviceId, StringComparison.Ordinal)
            || receipt.KeyVersion != notice.CurrentKeyVersion
            || !string.Equals(receipt.Challenge, notice.Challenge, StringComparison.Ordinal)
            || Math.Abs(now - receipt.AppliedAtUnixMs) > MaximumClockSkewMs)
            return false;

        var expected = ComputeProof(
            expectedNewDeviceSecret,
            notice,
            receipt with { Proof = string.Empty },
            pcName);
        return FixedEquals(expected, receipt.Proof);
    }

    private static string ComputeProof(
        string secret,
        DeviceKeyRotationNotice notice,
        DeviceKeyRotationReceipt receipt,
        string pcName)
    {
        var key = Encoding.UTF8.GetBytes(secret);
        var payload = Encoding.UTF8.GetBytes(string.Join('\n', new[]
        {
            "LabKom.KeyRotation.v1",
            notice.DeviceId,
            notice.CurrentKeyVersion.ToString(CultureInfo.InvariantCulture),
            notice.IssuedAtUnixMs.ToString(CultureInfo.InvariantCulture),
            receipt.AppliedAtUnixMs.ToString(CultureInfo.InvariantCulture),
            notice.Challenge,
            pcName.ToLowerInvariant(),
        }));
        try
        {
            return Convert.ToBase64String(HMACSHA256.HashData(key, payload));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(payload);
        }
    }

    private static bool IsChallengeValid(string challenge)
    {
        try
        {
            return Convert.FromBase64String(challenge).Length == 32;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static bool FixedEquals(string expected, string supplied)
    {
        var expectedBytes = Encoding.ASCII.GetBytes(expected);
        var suppliedBytes = Encoding.ASCII.GetBytes(supplied ?? string.Empty);
        return expectedBytes.Length == suppliedBytes.Length
               && CryptographicOperations.FixedTimeEquals(
                   expectedBytes,
                   suppliedBytes);
    }
}
