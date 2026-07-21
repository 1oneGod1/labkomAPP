using LabKom.Shared.Contracts;
using LabKom.Shared.Security;

namespace LabKom.Tests;

public sealed class ClassroomFeatureContractTests
{
    [Fact]
    public void UsageSample_ContainsOnlyBoundedActivityCounts()
    {
        var record = ActivityRecord.Usage(
            "PC-01",
            "Materi - Chrome",
            "chrome",
            ActivityCategory.WebBrowser,
            42,
            1_500);

        Assert.True(ContractValidation.IsValidActivity(record, "PC-01"));
        Assert.Null(record.Metrics is null
            ? "missing"
            : null);
        Assert.DoesNotContain("key", record.Title, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UsageSample_RejectsUnboundedKeyboardCount()
    {
        var record = ActivityRecord.Usage(
            "PC-01",
            "Editor",
            "notepad",
            ActivityCategory.Application,
            20_001,
            0);

        Assert.False(ContractValidation.IsValidActivity(record, "PC-01"));
    }

    [Fact]
    public void FileCollectionRequest_IsTargetBoundAndRejectsTraversal()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var valid = new FileCollectionRequest(
            Guid.NewGuid().ToString("N"),
            "PC-01",
            FileCollectionRoot.Documents,
            "KelasA\\Tugas.docx",
            5 * 1024 * 1024,
            now,
            now + 120_000);

        Assert.True(
            ContractValidation.IsValidFileCollectionRequest(
                valid,
                "PC-01",
                now));
        Assert.False(
            ContractValidation.IsValidFileCollectionRequest(
                valid,
                "PC-02",
                now));
        Assert.False(
            ContractValidation.IsValidFileCollectionRequest(
                valid with { RelativePath = "..\\secret.txt" },
                "PC-01",
                now));
    }

    [Fact]
    public void FileCollectionChunk_RequiresConsistentTerminalShape()
    {
        var requestId = Guid.NewGuid().ToString("N");
        var collecting = new FileCollectionChunk(
            requestId,
            "PC-01",
            FileCollectionState.Collecting,
            1,
            3,
            "Tugas.txt",
            new byte[] { 1, 2, 3 },
            null,
            null,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        var complete = collecting with
        {
            State = FileCollectionState.Completed,
            SequenceNumber = 2,
            Data = Array.Empty<byte>(),
            Sha256 = new string('A', 64),
        };

        Assert.True(
            ContractValidation.IsValidFileCollectionChunk(
                collecting,
                "PC-01"));
        Assert.True(
            ContractValidation.IsValidFileCollectionChunk(
                complete,
                "PC-01"));
        Assert.False(
            ContractValidation.IsValidFileCollectionChunk(
                complete with { Data = new byte[] { 9 } },
                "PC-01"));
    }

    [Fact]
    public void LessonSnapshot_IsReplayableOnlyForItsAudience()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var lesson = new LessonSnapshot(
            Guid.NewGuid().ToString("N"),
            Guid.NewGuid().ToString("N"),
            "Lab A",
            "Informatika",
            LessonPhase.Registration,
            new[] { "PC-01" },
            null,
            now,
            now + 600_000);

        Assert.True(
            ContractValidation.IsValidLessonSnapshot(
                lesson,
                "PC-01",
                now));
        Assert.False(
            ContractValidation.IsValidLessonSnapshot(
                lesson,
                "PC-02",
                now));
    }

    [Fact]
    public void AssessmentSubmission_RejectsDuplicateQuestionIds()
    {
        var questionId = Guid.NewGuid().ToString("N");
        var submission = new AssessmentSubmission(
            Guid.NewGuid().ToString("N"),
            Guid.NewGuid().ToString("N"),
            "PC-01",
            "12345",
            new[]
            {
                new AssessmentAnswer(questionId, 0),
                new AssessmentAnswer(questionId, 1),
            },
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

        Assert.False(
            ContractValidation.IsValidAssessmentSubmission(
                submission,
                "PC-01"));
    }

    [Theory]
    [InlineData(
        LabKomRole.Instructor,
        TeacherPermission.CollectFiles,
        true)]
    [InlineData(
        LabKomRole.Instructor,
        TeacherPermission.ManageLessons,
        true)]
    [InlineData(
        LabKomRole.Instructor,
        TeacherPermission.TechnicianConsole,
        false)]
    [InlineData(
        LabKomRole.Administrator,
        TeacherPermission.TechnicianConsole,
        true)]
    public void NewPermissions_FollowLeastPrivilege(
        LabKomRole role,
        TeacherPermission permission,
        bool expected)
    {
        Assert.Equal(expected, RbacPolicy.IsAllowed(role, permission));
    }
}
