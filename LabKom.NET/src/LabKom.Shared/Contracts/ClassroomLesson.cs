namespace LabKom.Shared.Contracts;

public enum LessonPhase
{
    Registration = 1,
    Teaching = 2,
    Assessment = 3,
    Ended = 4,
}

public sealed record AssessmentQuestionDelivery(
    string QuestionId,
    string Prompt,
    IReadOnlyList<string> Choices);

public sealed record AssessmentDelivery(
    string AssessmentId,
    string Title,
    int DurationMinutes,
    IReadOnlyList<AssessmentQuestionDelivery> Questions);

/// <summary>State lesson yang dapat direplay setelah Desktop reconnect.</summary>
public sealed record LessonSnapshot(
    string LessonId,
    string RoomId,
    string RoomName,
    string LessonTitle,
    LessonPhase Phase,
    IReadOnlyList<string> TargetPcNames,
    AssessmentDelivery? Assessment,
    long StartedAtUnixMs,
    long ExpiresAtUnixMs);

public sealed record StudentRegistrationSubmission(
    string LessonId,
    string PcName,
    string StudentCode,
    string FullName,
    long TimestampUnixMs);

public sealed record AssessmentAnswer(
    string QuestionId,
    int SelectedChoiceIndex);

public sealed record AssessmentSubmission(
    string AssessmentId,
    string LessonId,
    string PcName,
    string StudentCode,
    IReadOnlyList<AssessmentAnswer> Answers,
    long TimestampUnixMs);
