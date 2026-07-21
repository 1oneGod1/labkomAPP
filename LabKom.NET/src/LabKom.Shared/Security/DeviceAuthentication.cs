using System.Runtime.Versioning;
using LabKom.Shared.Hub;

namespace LabKom.Shared.Security;

[SupportedOSPlatform("windows")]
public static class DeviceAuthentication
{
    public static DeviceAuthenticationResult Validate(
        string rootSecret,
        string classroomId,
        string? suppliedSecret,
        string? deviceId,
        string pcName,
        int? keyVersion,
        int currentKeyVersion,
        int acceptedPreviousVersions,
        bool allowLegacy)
    {
        if (!HubSecurity.IsValidPcName(pcName)
            || currentKeyVersion <= 0
            || acceptedPreviousVersions < 0)
            return DeviceAuthenticationResult.Rejected("identity-invalid");

        if (string.IsNullOrWhiteSpace(deviceId) || !keyVersion.HasValue)
        {
            return allowLegacy && HubSecurity.IsValidSecret(rootSecret, suppliedSecret)
                ? new DeviceAuthenticationResult(true, true, null, null, "legacy")
                : DeviceAuthenticationResult.Rejected("legacy-disabled-or-invalid");
        }

        if (!Guid.TryParseExact(deviceId, "N", out _)
            || keyVersion <= 0
            || keyVersion > currentKeyVersion
            || keyVersion < currentKeyVersion - acceptedPreviousVersions)
            return DeviceAuthenticationResult.Rejected("key-version-rejected");

        var expected = DeviceCredentialStore.DeriveSecret(
            rootSecret,
            classroomId,
            deviceId,
            pcName,
            keyVersion.Value);
        return HubSecurity.IsValidSecret(expected, suppliedSecret)
            ? new DeviceAuthenticationResult(true, false, deviceId, keyVersion, "device")
            : DeviceAuthenticationResult.Rejected("credential-invalid");
    }
}

public sealed record DeviceAuthenticationResult(
    bool Success,
    bool IsLegacy,
    string? DeviceId,
    int? KeyVersion,
    string Reason)
{
    public static DeviceAuthenticationResult Rejected(string reason) =>
        new(false, false, null, null, reason);
}
