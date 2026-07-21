using LabKom.Shared.Contracts;
using LabKom.Shared.Hub;
using LabKom.Shared.Security;
using LabKom.Teacher.Hub;
using Microsoft.AspNetCore.SignalR;

namespace LabKom.Teacher.Services;

public sealed partial class ClassroomLessonService
{
    private readonly HubContextHolder _holder;
    private readonly TeacherAuthorizationService _authorization;
    private readonly ActivityFeed _activity;
    private readonly ClassroomLessonStore _store;

    public event EventHandler? Changed;

    public ClassroomLessonService(
        HubContextHolder holder,
        TeacherAuthorizationService authorization,
        ActivityFeed activity,
        ClassroomLessonStore store)
    {
        _holder = holder;
        _authorization = authorization;
        _activity = activity;
        _store = store;
    }

    private IHubContext<TeacherHub> Hub =>
        _holder.HubContext
        ?? throw new InvalidOperationException("Hub belum siap.");

    public LessonSnapshot? ActiveLesson =>
        _store.Snapshot().ActiveLesson;

    public IReadOnlyList<StudentRegistrationSubmission> Registrations =>
        _store.Snapshot().Registrations
            .OrderByDescending(item => item.TimestampUnixMs)
            .ToArray();

    public IReadOnlyList<AssessmentResultRecord> Results =>
        _store.Snapshot().Results
            .OrderByDescending(item => item.SubmittedAtUnixMs)
            .ToArray();

    public Task<LessonSnapshot> StartLessonAsync(
        string roomName,
        string lessonTitle,
        IReadOnlyCollection<string> targetPcNames) =>
        _authorization.ExecuteAsync(
            TeacherPermission.ManageLessons,
            "lesson.start",
            TargetLabel(targetPcNames),
            async () =>
            {
                var now = DateTimeOffset.UtcNow;
                var snapshot = new LessonSnapshot(
                    Guid.NewGuid().ToString("N"),
                    Guid.NewGuid().ToString("N"),
                    roomName.Trim(),
                    lessonTitle.Trim(),
                    LessonPhase.Registration,
                    NormalizeTargets(targetPcNames),
                    null,
                    now.ToUnixTimeMilliseconds(),
                    now.AddHours(4).ToUnixTimeMilliseconds());
                ValidateSnapshot(snapshot);
                _store.SetActive(snapshot);
                await BroadcastAsync(snapshot);
                Changed?.Invoke(this, EventArgs.Empty);
                return snapshot;
            });

    public Task<LessonSnapshot> BeginTeachingAsync() =>
        UpdateAsync(
            "lesson.teaching",
            current => current with
            {
                Phase = LessonPhase.Teaching,
                Assessment = null,
            },
            Array.Empty<AssessmentAnswerKey>());

    public Task<LessonSnapshot> StartAssessmentAsync(
        string title,
        int durationMinutes,
        IReadOnlyList<AssessmentQuestionDraft> questions)
    {
        if (string.IsNullOrWhiteSpace(title)
            || title.Length > 120
            || durationMinutes is < 1 or > 240
            || questions.Count is <= 0
                or > ContractValidation.MaximumAssessmentQuestions)
        {
            throw new ArgumentException(
                "Judul, durasi, atau jumlah soal tidak valid.");
        }

        var assessmentId = Guid.NewGuid().ToString("N");
        var delivery = new List<AssessmentQuestionDelivery>();
        var keys = new List<AssessmentAnswerKey>();
        foreach (var draft in questions)
        {
            if (string.IsNullOrWhiteSpace(draft.Prompt)
                || draft.Prompt.Length > 1_000
                || draft.Choices.Count is < 2
                    or > ContractValidation.MaximumAssessmentChoices
                || draft.Choices.Any(choice =>
                    string.IsNullOrWhiteSpace(choice)
                    || choice.Length > 500)
                || draft.CorrectChoiceIndex < 0
                || draft.CorrectChoiceIndex >= draft.Choices.Count)
            {
                throw new ArgumentException(
                    "Pertanyaan, pilihan, atau kunci jawaban tidak valid.");
            }

            var questionId = Guid.NewGuid().ToString("N");
            delivery.Add(
                new AssessmentQuestionDelivery(
                    questionId,
                    draft.Prompt.Trim(),
                    draft.Choices.Select(choice => choice.Trim()).ToArray()));
            keys.Add(
                new AssessmentAnswerKey(
                    questionId,
                    draft.CorrectChoiceIndex));
        }

        var assessment = new AssessmentDelivery(
            assessmentId,
            title.Trim(),
            durationMinutes,
            delivery);
        return UpdateAsync(
            "assessment.start",
            current => current with
            {
                Phase = LessonPhase.Assessment,
                Assessment = assessment,
                ExpiresAtUnixMs = Math.Min(
                    current.ExpiresAtUnixMs,
                    DateTimeOffset.UtcNow
                        .AddMinutes(durationMinutes)
                        .ToUnixTimeMilliseconds()),
            },
            keys);
    }

    public Task EndLessonAsync() =>
        _authorization.ExecuteAsync(
            TeacherPermission.ManageLessons,
            "lesson.end",
            ActiveLesson?.LessonId,
            async () =>
            {
                var current = ActiveLesson
                    ?? throw new InvalidOperationException(
                        "Tidak ada lesson aktif.");
                var ended = current with
                {
                    Phase = LessonPhase.Ended,
                    Assessment = null,
                    ExpiresAtUnixMs = Math.Max(
                        current.StartedAtUnixMs + 1,
                        DateTimeOffset.UtcNow
                            .AddMinutes(1)
                            .ToUnixTimeMilliseconds()),
                };
                await BroadcastAsync(ended);
                _store.SetActive(null);
                Changed?.Invoke(this, EventArgs.Empty);
            });

    private Task<LessonSnapshot> UpdateAsync(
        string action,
        Func<LessonSnapshot, LessonSnapshot> update,
        IReadOnlyList<AssessmentAnswerKey> keys) =>
        _authorization.ExecuteAsync(
            TeacherPermission.ManageLessons,
            action,
            ActiveLesson?.LessonId,
            async () =>
            {
                var current = ActiveLesson
                    ?? throw new InvalidOperationException(
                        "Tidak ada lesson aktif.");
                var snapshot = update(current);
                ValidateSnapshot(snapshot);
                _store.SetActive(snapshot, keys);
                await BroadcastAsync(snapshot);
                Changed?.Invoke(this, EventArgs.Empty);
                return snapshot;
            });

    private Task BroadcastAsync(LessonSnapshot snapshot)
    {
        if (snapshot.TargetPcNames.Count == 0)
        {
            return Hub.Clients
                .Group(HubRoutes.Groups.ForRole(HubRoutes.Roles.Desktop))
                .SendAsync(
                    HubRoutes.Methods.ReceiveLessonSnapshot,
                    snapshot);
        }

        return Task.WhenAll(snapshot.TargetPcNames.Select(pcName =>
            Hub.Clients
                .Group(HubRoutes.Groups.ForPcRole(
                    pcName,
                    HubRoutes.Roles.Desktop))
                .SendAsync(
                    HubRoutes.Methods.ReceiveLessonSnapshot,
                    snapshot)));
    }

    private static void ValidateSnapshot(LessonSnapshot snapshot)
    {
        var validationPc = snapshot.TargetPcNames.FirstOrDefault()
            ?? Environment.MachineName;
        if (!ContractValidation.IsValidLessonSnapshot(
                snapshot,
                validationPc))
        {
            throw new ArgumentException("State lesson tidak valid.");
        }
    }

    private static string[] NormalizeTargets(
        IReadOnlyCollection<string> targets) =>
        targets
            .Where(HubSecurity.IsValidPcName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .Take(ContractValidation.MaximumLessonTargets)
            .ToArray();

    private static string TargetLabel(
        IReadOnlyCollection<string> targets) =>
        targets.Count == 0 ? "all" : string.Join(",", targets.Take(16));
}
