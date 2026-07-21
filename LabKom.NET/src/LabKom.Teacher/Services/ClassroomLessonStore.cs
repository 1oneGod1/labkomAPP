using System.IO;
using System.Text.Json;
using LabKom.Shared.Contracts;

namespace LabKom.Teacher.Services;

/// <summary>Penyimpanan atomik untuk lesson, register, kunci, dan hasil.</summary>
public sealed class ClassroomLessonStore
{
    private const int SchemaVersion = 1;
    private readonly object _sync = new();
    private readonly string _path;
    private ClassroomLessonDocument _document;

    public ClassroomLessonStore()
    {
        _path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LabKom",
            "Classroom",
            "lessons.json");
        _document = Load();
        if (_document.ActiveLesson is { } active
            && active.ExpiresAtUnixMs
                < DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
        {
            _document = _document with
            {
                ActiveLesson = null,
                ActiveAnswerKeys = Array.Empty<AssessmentAnswerKey>(),
            };
            SaveUnsafe();
        }
    }

    public ClassroomLessonDocument Snapshot()
    {
        lock (_sync) return _document;
    }

    public void SetActive(
        LessonSnapshot? lesson,
        IReadOnlyList<AssessmentAnswerKey>? keys = null)
    {
        lock (_sync)
        {
            _document = _document with
            {
                ActiveLesson = lesson,
                ActiveAnswerKeys = keys
                    ?? Array.Empty<AssessmentAnswerKey>(),
            };
            SaveUnsafe();
        }
    }

    public void UpsertRegistration(
        StudentRegistrationSubmission submission)
    {
        lock (_sync)
        {
            _document = _document with
            {
                Registrations = _document.Registrations
                    .Where(item =>
                        item.LessonId != submission.LessonId
                        || !string.Equals(
                            item.PcName,
                            submission.PcName,
                            StringComparison.OrdinalIgnoreCase))
                    .Append(submission)
                    .TakeLast(5_000)
                    .ToArray(),
            };
            SaveUnsafe();
        }
    }

    public void UpsertResult(AssessmentResultRecord result)
    {
        lock (_sync)
        {
            _document = _document with
            {
                Results = _document.Results
                    .Where(item =>
                        item.AssessmentId != result.AssessmentId
                        || !string.Equals(
                            item.PcName,
                            result.PcName,
                            StringComparison.OrdinalIgnoreCase))
                    .Append(result)
                    .TakeLast(5_000)
                    .ToArray(),
            };
            SaveUnsafe();
        }
    }

    private ClassroomLessonDocument Load()
    {
        if (!File.Exists(_path))
            return ClassroomLessonDocument.Empty;
        try
        {
            var value = JsonSerializer.Deserialize<ClassroomLessonDocument>(
                File.ReadAllText(_path),
                JsonOptions);
            return value is { SchemaVersion: SchemaVersion }
                ? value
                : ClassroomLessonDocument.Empty;
        }
        catch
        {
            return ClassroomLessonDocument.Empty;
        }
    }

    private void SaveUnsafe()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(
                temporary,
                JsonSerializer.Serialize(_document, JsonOptions));
            File.Move(temporary, _path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };
}

public sealed record AssessmentQuestionDraft(
    string Prompt,
    IReadOnlyList<string> Choices,
    int CorrectChoiceIndex);

public sealed record AssessmentAnswerKey(
    string QuestionId,
    int CorrectChoiceIndex);

public sealed record AssessmentResultRecord(
    string AssessmentId,
    string LessonId,
    string PcName,
    string StudentCode,
    int CorrectAnswers,
    int TotalQuestions,
    long SubmittedAtUnixMs);

public sealed record ClassroomLessonDocument(
    int SchemaVersion,
    LessonSnapshot? ActiveLesson,
    IReadOnlyList<AssessmentAnswerKey> ActiveAnswerKeys,
    IReadOnlyList<StudentRegistrationSubmission> Registrations,
    IReadOnlyList<AssessmentResultRecord> Results)
{
    public static ClassroomLessonDocument Empty => new(
        1,
        null,
        Array.Empty<AssessmentAnswerKey>(),
        Array.Empty<StudentRegistrationSubmission>(),
        Array.Empty<AssessmentResultRecord>());
}
