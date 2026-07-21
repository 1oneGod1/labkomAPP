using LabKom.Shared.Contracts;

namespace LabKom.Teacher.Services;

public sealed partial class ClassroomLessonService
{
    public LessonSnapshot? ReplayFor(string pcName)
    {
        var active = ActiveLesson;
        if (active is null
            || active.Phase == LessonPhase.Ended
            || active.ExpiresAtUnixMs
                < DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
        {
            return null;
        }

        return IsTarget(active, pcName) ? active : null;
    }

    public bool Register(
        StudentRegistrationSubmission submission,
        string connectedPcName)
    {
        if (!ContractValidation.IsValidStudentRegistration(
                submission,
                connectedPcName))
        {
            return false;
        }

        var active = ActiveLesson;
        if (active is null
            || active.LessonId != submission.LessonId
            || DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                > active.ExpiresAtUnixMs
            || !IsSubmissionTimeValid(active, submission.TimestampUnixMs)
            || !IsTarget(active, connectedPcName))
        {
            return false;
        }

        _store.UpsertRegistration(submission);
        _authorization.RecordSystemEvent(
            "lesson.register",
            connectedPcName,
            "accepted",
            $"{submission.LessonId}|{submission.StudentCode}");
        _activity.Push(
            new ActivityRecord(
                connectedPcName,
                ActivityRecordKind.Registration,
                $"Register: {submission.FullName}",
                submission.StudentCode,
                submission.TimestampUnixMs));
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public bool SubmitAssessment(
        AssessmentSubmission submission,
        string connectedPcName)
    {
        if (!ContractValidation.IsValidAssessmentSubmission(
                submission,
                connectedPcName))
        {
            return false;
        }

        var document = _store.Snapshot();
        var active = document.ActiveLesson;
        if (active?.Assessment is null
            || active.Phase != LessonPhase.Assessment
            || active.LessonId != submission.LessonId
            || active.Assessment.AssessmentId
                != submission.AssessmentId
            || DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                > active.ExpiresAtUnixMs + 30_000
            || !IsSubmissionTimeValid(active, submission.TimestampUnixMs)
            || !IsTarget(active, connectedPcName))
        {
            return false;
        }

        var questions = active.Assessment.Questions.ToDictionary(
            question => question.QuestionId,
            StringComparer.Ordinal);
        if (submission.Answers.Count != questions.Count
            || submission.Answers.Any(answer =>
                !questions.TryGetValue(
                    answer.QuestionId,
                    out var question)
                || answer.SelectedChoiceIndex
                    >= question.Choices.Count))
        {
            return false;
        }

        var keys = document.ActiveAnswerKeys.ToDictionary(
            key => key.QuestionId,
            StringComparer.Ordinal);
        var correct = submission.Answers.Count(answer =>
            keys.TryGetValue(answer.QuestionId, out var key)
            && key.CorrectChoiceIndex
                == answer.SelectedChoiceIndex);
        var result = new AssessmentResultRecord(
            submission.AssessmentId,
            submission.LessonId,
            submission.PcName,
            submission.StudentCode,
            correct,
            submission.Answers.Count,
            submission.TimestampUnixMs);
        _store.UpsertResult(result);
        _authorization.RecordSystemEvent(
            "assessment.submit",
            connectedPcName,
            "accepted",
            $"{result.AssessmentId}|" +
            $"{result.CorrectAnswers}/{result.TotalQuestions}");
        _activity.Push(
            new ActivityRecord(
                connectedPcName,
                ActivityRecordKind.Assessment,
                $"Assessment: {result.CorrectAnswers}/" +
                $"{result.TotalQuestions}",
                result.StudentCode,
                result.SubmittedAtUnixMs));
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    private static bool IsSubmissionTimeValid(
        LessonSnapshot lesson,
        long timestampUnixMs)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return timestampUnixMs
                   >= lesson.StartedAtUnixMs - 30_000
               && timestampUnixMs
                   <= lesson.ExpiresAtUnixMs + 30_000
               && timestampUnixMs <= now + 30_000;
    }


    private static bool IsTarget(
        LessonSnapshot snapshot,
        string pcName) =>
        snapshot.TargetPcNames.Count == 0
        || snapshot.TargetPcNames.Any(target =>
            string.Equals(
                target,
                pcName,
                StringComparison.OrdinalIgnoreCase));
}
