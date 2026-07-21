using LabKom.Shared.Contracts;
using LabKom.Student.Desktop.Services;
using LabKom.Teacher.Services;

namespace LabKom.Tests;

public sealed class RecoveryAndFileTransferTests
{
    [Fact]
    public void EmptyClassroomSnapshotExplicitlyRepresentsUnlockedState()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var snapshot = new ClassroomStateSnapshot(
            Guid.NewGuid().ToString("N"),
            Attention: null,
            Broadcast: null,
            now);

        Assert.True(ContractValidation.IsValidClassroomStateSnapshot(
            snapshot,
            "PC-SISWA",
            now));
    }

    [Fact]
    public void ClassroomSnapshotRejectsExpiredOrInactiveNestedState()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var sessionId = Guid.NewGuid().ToString("N");
        var expired = new ClassroomStateSnapshot(
            sessionId,
            Attention: null,
            Broadcast: null,
            now - (ContractValidation.MaximumStateSnapshotAgeSeconds + 1) * 1_000L);
        var inactiveAttention = new ClassroomStateSnapshot(
            sessionId,
            AttentionCommand.For(
                "PC-SISWA",
                enabled: false,
                message: string.Empty),
            Broadcast: null,
            now);
        var inactiveBroadcast = new ClassroomStateSnapshot(
            sessionId,
            Attention: null,
            new TeacherBroadcastSignal(
                Guid.NewGuid().ToString("N"),
                Active: false,
                Paused: false),
            now);

        Assert.False(ContractValidation.IsValidClassroomStateSnapshot(
            expired,
            "PC-SISWA",
            now));
        Assert.False(ContractValidation.IsValidClassroomStateSnapshot(
            inactiveAttention,
            "PC-SISWA",
            now));
        Assert.False(ContractValidation.IsValidClassroomStateSnapshot(
            inactiveBroadcast,
            "PC-SISWA",
            now));
    }

    [Fact]
    public void TeacherSessionIdentityIsStableAndCreatesValidSnapshot()
    {
        var identity = new ClassroomSessionIdentity();

        var first = identity.CreateSnapshot(null, null);
        var second = identity.CreateSnapshot(null, null);

        Assert.Equal(first.SessionId, second.SessionId);
        Assert.Matches("^[0-9a-f]{32}$", identity.SessionId);
        Assert.True(ContractValidation.IsValidClassroomStateSnapshot(
            first,
            "PC-SISWA"));
    }

    [Fact]
    public void AvailableDestinationPreservesExistingFile()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "LabKom.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var existing = Path.Combine(root, "materi.pdf");
            File.WriteAllText(existing, "lama");

            var destination = FileDownloader.GetAvailableDestinationPath(
                root,
                "materi.pdf");

            Assert.Equal(Path.Combine(root, "materi (1).pdf"), destination);
            Assert.Equal("lama", File.ReadAllText(existing));
        }
        finally
        {
            var fullRoot = Path.GetFullPath(root);
            var tempRoot = Path.GetFullPath(Path.GetTempPath());
            if (fullRoot.StartsWith(
                    tempRoot,
                    StringComparison.OrdinalIgnoreCase))
            {
                Directory.Delete(fullRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void AvailableDestinationAlsoAvoidsDirectoryNameConflict()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "LabKom.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            Directory.CreateDirectory(Path.Combine(root, "materi.pdf"));

            var destination = FileDownloader.GetAvailableDestinationPath(
                root,
                "materi.pdf");

            Assert.Equal(Path.Combine(root, "materi (1).pdf"), destination);
        }
        finally
        {
            var fullRoot = Path.GetFullPath(root);
            var tempRoot = Path.GetFullPath(Path.GetTempPath());
            if (fullRoot.StartsWith(
                    tempRoot,
                    StringComparison.OrdinalIgnoreCase))
            {
                Directory.Delete(fullRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void FileProgressValidationRejectsSpoofedOrUnboundedPayloads()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var valid = new FileDistributionProgress(
            Guid.NewGuid().ToString("N"),
            "PC-SISWA",
            FileDistributionState.Failed,
            1_024,
            "Jaringan terputus",
            now);

        Assert.True(ContractValidation.IsValidFileProgress(
            valid,
            "PC-SISWA",
            now));
        Assert.False(ContractValidation.IsValidFileProgress(
            valid with { NoticeId = "bukan-guid" },
            "PC-SISWA",
            now));
        Assert.False(ContractValidation.IsValidFileProgress(
            valid with
            {
                BytesReceived =
                    ContractValidation.MaximumFileTransferBytes + 1,
            },
            "PC-SISWA",
            now));
        Assert.False(ContractValidation.IsValidFileProgress(
            valid with
            {
                State = FileDistributionState.Completed,
                ErrorMessage = "tidak boleh",
            },
            "PC-SISWA",
            now));
    }
}