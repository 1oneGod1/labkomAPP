using LabKom.Shared.Hub;

namespace LabKom.Shared.Contracts;

public static partial class ContractValidation
{
    public const long MaximumFileCollectionBytes = 20L * 1024 * 1024;
    public const int MaximumFileCollectionChunkBytes = 256 * 1024;
    public const int MaximumAssessmentQuestions = 100;
    public const int MaximumAssessmentChoices = 8;
    public const int MaximumLessonTargets = 256;

    public static bool IsValidFileCollectionRequest(
        FileCollectionRequest? request,
        string expectedPcName,
        long? nowUnixMs = null)
    {
        if (request is null
            || !Guid.TryParseExact(request.RequestId, "N", out _)
            || !MatchesPc(request.TargetPcName, expectedPcName)
            || !Enum.IsDefined(request.Root)
            || request.MaximumBytes is <= 0 or > MaximumFileCollectionBytes
            || request.RequestedAtUnixMs <= 0
            || request.ExpiresAtUnixMs <= request.RequestedAtUnixMs
            || request.ExpiresAtUnixMs - request.RequestedAtUnixMs
                > MaximumCommandTtlSeconds * 1_000L
            || !IsSafeRelativeFilePath(request.RelativePath))
        {
            return false;
        }

        var now = nowUnixMs ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return request.RequestedAtUnixMs <= now + MaximumClockSkewSeconds * 1_000L
               && request.ExpiresAtUnixMs >= now;
    }

    public static bool IsValidFileCollectionChunk(
        FileCollectionChunk? chunk,
        string expectedPcName)
    {
        if (chunk is null
            || !Guid.TryParseExact(chunk.RequestId, "N", out _)
            || !MatchesPc(chunk.PcName, expectedPcName)
            || !Enum.IsDefined(chunk.State)
            || chunk.SequenceNumber is <= 0 or > 10_000
            || chunk.TotalBytes is < 0 or > MaximumFileCollectionBytes
            || string.IsNullOrWhiteSpace(chunk.FileName)
            || chunk.FileName.Length > 128
            || !string.Equals(
                Path.GetFileName(chunk.FileName),
                chunk.FileName,
                StringComparison.Ordinal)
            || chunk.Data is null
            || chunk.Data.Length > MaximumFileCollectionChunkBytes
            || chunk.TimestampUnixMs <= 0
            || chunk.Message?.Length > MaximumCommandMessageLength)
        {
            return false;
        }

        return chunk.State switch
        {
            FileCollectionState.Collecting =>
                chunk.Data.Length > 0
                && chunk.Sha256 is null
                && chunk.Message is null,
            FileCollectionState.Completed =>
                chunk.Data.Length == 0
                && IsSha256(chunk.Sha256)
                && chunk.Message is null,
            FileCollectionState.Rejected or FileCollectionState.Failed =>
                chunk.Data.Length == 0
                && chunk.Sha256 is null
                && !string.IsNullOrWhiteSpace(chunk.Message),
            _ => false,
        };
    }

    public static bool IsValidLessonSnapshot(
        LessonSnapshot? snapshot,
        string expectedPcName,
        long? nowUnixMs = null)
    {
        if (snapshot is null
            || !Guid.TryParseExact(snapshot.LessonId, "N", out _)
            || !Guid.TryParseExact(snapshot.RoomId, "N", out _)
            || string.IsNullOrWhiteSpace(snapshot.RoomName)
            || snapshot.RoomName.Length > 80
            || string.IsNullOrWhiteSpace(snapshot.LessonTitle)
            || snapshot.LessonTitle.Length > 120
            || !Enum.IsDefined(snapshot.Phase)
            || snapshot.TargetPcNames is null
            || snapshot.TargetPcNames.Count > MaximumLessonTargets
            || snapshot.StartedAtUnixMs <= 0
            || snapshot.ExpiresAtUnixMs <= snapshot.StartedAtUnixMs
            || snapshot.ExpiresAtUnixMs - snapshot.StartedAtUnixMs
                > TimeSpan.FromHours(12).TotalMilliseconds
            || !IsValidTargets(snapshot.TargetPcNames)
            || (snapshot.TargetPcNames.Count > 0
                && !snapshot.TargetPcNames.Any(
                    pc => MatchesPc(pc, expectedPcName)))
            || !IsValidAssessmentDelivery(snapshot.Assessment))
        {
            return false;
        }

        if (snapshot.Phase == LessonPhase.Assessment
            && snapshot.Assessment is null)
        {
            return false;
        }

        var now = nowUnixMs ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return snapshot.StartedAtUnixMs
                   <= now + MaximumClockSkewSeconds * 1_000L
               && snapshot.ExpiresAtUnixMs >= now;
    }

    public static bool IsValidStudentRegistration(
        StudentRegistrationSubmission? submission,
        string expectedPcName)
    {
        return submission is not null
               && Guid.TryParseExact(submission.LessonId, "N", out _)
               && MatchesPc(submission.PcName, expectedPcName)
               && IsSimpleText(submission.StudentCode, 32)
               && IsSimpleText(submission.FullName, 120)
               && submission.TimestampUnixMs > 0;
    }

    public static bool IsValidAssessmentSubmission(
        AssessmentSubmission? submission,
        string expectedPcName)
    {
        if (submission is null
            || !Guid.TryParseExact(submission.AssessmentId, "N", out _)
            || !Guid.TryParseExact(submission.LessonId, "N", out _)
            || !MatchesPc(submission.PcName, expectedPcName)
            || !IsSimpleText(submission.StudentCode, 32)
            || submission.Answers is null
            || submission.Answers.Count is <= 0 or > MaximumAssessmentQuestions
            || submission.TimestampUnixMs <= 0)
        {
            return false;
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        return submission.Answers.All(answer =>
            Guid.TryParseExact(answer.QuestionId, "N", out _)
            && answer.SelectedChoiceIndex is >= 0 and < MaximumAssessmentChoices
            && ids.Add(answer.QuestionId));
    }

    private static bool IsValidAssessmentDelivery(
        AssessmentDelivery? assessment)
    {
        if (assessment is null) return true;
        if (!Guid.TryParseExact(assessment.AssessmentId, "N", out _)
            || !IsSimpleText(assessment.Title, 120)
            || assessment.DurationMinutes is < 1 or > 240
            || assessment.Questions is null
            || assessment.Questions.Count is <= 0 or > MaximumAssessmentQuestions)
        {
            return false;
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        return assessment.Questions.All(question =>
            Guid.TryParseExact(question.QuestionId, "N", out _)
            && ids.Add(question.QuestionId)
            && IsSimpleText(question.Prompt, 1_000)
            && question.Choices is not null
            && question.Choices.Count is >= 2 and <= MaximumAssessmentChoices
            && question.Choices.All(choice => IsSimpleText(choice, 500)));
    }

    private static bool IsSafeRelativeFilePath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > 260
            || Path.IsPathRooted(value)
            || value.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
        {
            return false;
        }

        var parts = value.Split(
            new[] { '/', '\\' },
            StringSplitOptions.RemoveEmptyEntries);
        return parts.Length is > 0 and <= 8
               && parts.All(part => part is not "." and not "..");
    }

    private static bool IsValidTargets(IReadOnlyList<string> targets)
    {
        var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return targets.All(pc => HubSecurity.IsValidPcName(pc) && unique.Add(pc));
    }

    private static bool IsSimpleText(string? value, int maximumLength) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Length <= maximumLength
        && value.All(character =>
            character is not '\r' and not '\n' and not '\0');

    private static bool IsSha256(string? value) =>
        value is { Length: 64 }
        && value.All(character =>
            character is >= '0' and <= '9'
            or >= 'a' and <= 'f'
            or >= 'A' and <= 'F');
}
