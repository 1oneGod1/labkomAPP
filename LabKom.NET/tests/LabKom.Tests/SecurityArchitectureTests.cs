using LabKom.Shared.Contracts;
using LabKom.Shared.Security;

namespace LabKom.Tests;

public sealed class SecurityArchitectureTests
{
    private static readonly string RootSecret = new('r', 48);

    [Fact]
    public void DeviceCredentials_AreUniquePerDeviceAndVersion()
    {
        var classroomId = Guid.NewGuid().ToString("N");
        var firstDevice = Guid.NewGuid().ToString("N");
        var secondDevice = Guid.NewGuid().ToString("N");

        var firstV1 = DeviceCredentialStore.DeriveSecret(
            RootSecret, classroomId, firstDevice, "LAB-PC-01", 1);
        var firstV2 = DeviceCredentialStore.DeriveSecret(
            RootSecret, classroomId, firstDevice, "LAB-PC-01", 2);
        var secondV1 = DeviceCredentialStore.DeriveSecret(
            RootSecret, classroomId, secondDevice, "LAB-PC-02", 1);

        Assert.NotEqual(firstV1, firstV2);
        Assert.NotEqual(firstV1, secondV1);
        Assert.NotEqual(firstV2, secondV1);
    }

    [Fact]
    public void DeviceAuthentication_AcceptsCurrentAndGraceVersionOnly()
    {
        var classroomId = Guid.NewGuid().ToString("N");
        var deviceId = Guid.NewGuid().ToString("N");
        const string pcName = "LAB-PC-01";
        var current = DeviceCredentialStore.DeriveSecret(
            RootSecret, classroomId, deviceId, pcName, 3);
        var grace = DeviceCredentialStore.DeriveSecret(
            RootSecret, classroomId, deviceId, pcName, 2);
        var expired = DeviceCredentialStore.DeriveSecret(
            RootSecret, classroomId, deviceId, pcName, 1);

        Assert.True(DeviceAuthentication.Validate(
            RootSecret, classroomId, current, deviceId, pcName,
            keyVersion: 3, currentKeyVersion: 3,
            acceptedPreviousVersions: 1, allowLegacy: false).Success);
        Assert.True(DeviceAuthentication.Validate(
            RootSecret, classroomId, grace, deviceId, pcName,
            keyVersion: 2, currentKeyVersion: 3,
            acceptedPreviousVersions: 1, allowLegacy: false).Success);
        Assert.False(DeviceAuthentication.Validate(
            RootSecret, classroomId, expired, deviceId, pcName,
            keyVersion: 1, currentKeyVersion: 3,
            acceptedPreviousVersions: 1, allowLegacy: false).Success);
        Assert.False(DeviceAuthentication.Validate(
            RootSecret, classroomId, current, deviceId, "LAB-PC-02",
            keyVersion: 3, currentKeyVersion: 3,
            acceptedPreviousVersions: 1, allowLegacy: false).Success);
    }

    [Fact]
    public void KeyRotationReceipt_ProvesPossessionOfNewDeviceKey()
    {
        var classroomId = Guid.NewGuid().ToString("N");
        var deviceId = Guid.NewGuid().ToString("N");
        const string pcName = "LAB-PC-01";
        var notice = DeviceKeyRotationProtocol.CreateNotice(deviceId, 2);
        var newSecret = DeviceCredentialStore.DeriveSecret(
            RootSecret, classroomId, deviceId, pcName, 2);

        Assert.True(DeviceKeyRotationProtocol.IsValidNotice(
            notice, deviceId, currentDeviceVersion: 1));
        Assert.True(DeviceKeyRotationProtocol.IsValidNotice(
            notice, deviceId, currentDeviceVersion: 2));
        var receipt = DeviceKeyRotationProtocol.CreateReceipt(
            notice, newSecret, pcName);
        Assert.True(DeviceKeyRotationProtocol.ValidateReceipt(
            notice, receipt, newSecret, pcName));
        Assert.False(DeviceKeyRotationProtocol.ValidateReceipt(
            notice,
            receipt with { Proof = Convert.ToBase64String(new byte[32]) },
            newSecret,
            pcName));
    }

    [Theory]
    [InlineData(LabKomRole.Observer, TeacherPermission.ViewClassroom, true)]
    [InlineData(LabKomRole.Observer, TeacherPermission.SendMessage, false)]
    [InlineData(LabKomRole.Auditor, TeacherPermission.ViewAudit, true)]
    [InlineData(LabKomRole.Auditor, TeacherPermission.ManageAttention, false)]
    [InlineData(LabKomRole.Instructor, TeacherPermission.ManagePolicies, true)]
    [InlineData(LabKomRole.Instructor, TeacherPermission.ManagePower, true)]
    [InlineData(LabKomRole.Instructor, TeacherPermission.RemoteControl, true)]
    [InlineData(LabKomRole.Observer, TeacherPermission.RemoteControl, false)]
    [InlineData(LabKomRole.Instructor, TeacherPermission.ManageDevices, false)]
    [InlineData(LabKomRole.Administrator, TeacherPermission.EmergencyUnlock, true)]
    public void Rbac_EnforcesExpectedPermissionMatrix(
        LabKomRole role,
        TeacherPermission permission,
        bool expected)
    {
        Assert.Equal(expected, RbacPolicy.IsAllowed(role, permission));
    }

    [Fact]
    public void AuditJournal_DetectsModifiedEntry()
    {
        WithAuditJournal((journalPath, anchorPath) =>
        {
            var journal = new SecurityAuditJournal(
                RootSecret, journalPath, anchorPath, protectAnchor: false);
            journal.Append(
                "S-1-5-21-1", "LAB\\guru", LabKomRole.Instructor,
                "attention.lock", "LAB-PC-01", "authorized");

            var content = File.ReadAllText(journalPath);
            File.WriteAllText(
                journalPath,
                content.Replace(
                    "\"outcome\":\"authorized\"",
                    "\"outcome\":\"succeeded\"",
                    StringComparison.Ordinal));
            Assert.Throws<InvalidDataException>(() => journal.Append(
                "S-1-5-21-1", "LAB\\guru", LabKomRole.Instructor,
                "attention.unlock", "LAB-PC-01", "authorized"));


            var reopened = new SecurityAuditJournal(
                RootSecret, journalPath, anchorPath, protectAnchor: false);
            Assert.False(reopened.IntegrityValid);
            Assert.Throws<InvalidDataException>(() => reopened.Append(
                "S-1-5-21-1", "LAB\\guru", LabKomRole.Instructor,
                "attention.unlock", "LAB-PC-01", "authorized"));
        });
    }

    [Fact]
    public void AuditJournal_DetectsTruncationAgainstProtectedAnchor()
    {
        WithAuditJournal((journalPath, anchorPath) =>
        {
            var journal = new SecurityAuditJournal(
                RootSecret, journalPath, anchorPath, protectAnchor: false);
            journal.Append(
                "S-1-5-21-1", "LAB\\guru", LabKomRole.Instructor,
                "attention.lock", "LAB-PC-01", "authorized");
            journal.Append(
                "S-1-5-21-1", "LAB\\guru", LabKomRole.Instructor,
                "attention.unlock", "LAB-PC-01", "authorized");

            var firstLine = File.ReadLines(journalPath).First();
            File.WriteAllText(journalPath, firstLine + Environment.NewLine);

            var reopened = new SecurityAuditJournal(
                RootSecret, journalPath, anchorPath, protectAnchor: false);
            Assert.False(reopened.IntegrityValid);
        });
    }

    private static void WithAuditJournal(Action<string, string> action)
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "labkom-security-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            action(
                Path.Combine(directory, "security-audit.jsonl"),
                Path.Combine(directory, "security-audit.anchor"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
