using System.Windows;
using LabKom.Teacher.Services;

namespace LabKom.Teacher;

public partial class ClassroomConsoleWindow : Window
{
    private readonly ClassroomLessonService _lessons;
    private readonly string[] _targets;

    public ClassroomConsoleWindow(
        ClassroomLessonService lessons,
        IReadOnlyCollection<string> targets)
    {
        _lessons = lessons;
        _targets = targets
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        InitializeComponent();
        AudienceText.Text = _targets.Length == 0
            ? "Audiens: semua Desktop siswa"
            : $"Audiens: {_targets.Length} PC terpilih";
        _lessons.Changed += Lessons_Changed;
        Closed += (_, _) => _lessons.Changed -= Lessons_Changed;
        Refresh();
    }

    private void Lessons_Changed(object? sender, EventArgs e) =>
        Dispatcher.Invoke(Refresh);

    private async void StartLesson_Click(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            await _lessons.StartLessonAsync(
                RoomNameBox.Text,
                LessonTitleBox.Text,
                _targets);
            Refresh();
        }
        catch (Exception exception)
        {
            ShowError(exception);
        }
    }

    private async void Teaching_Click(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            await _lessons.BeginTeachingAsync();
            Refresh();
        }
        catch (Exception exception)
        {
            ShowError(exception);
        }
    }

    private async void StartAssessment_Click(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            if (!int.TryParse(DurationBox.Text, out var duration))
                throw new ArgumentException("Durasi harus berupa angka menit.");
            var drafts = ParseQuestions(QuestionsBox.Text);
            await _lessons.StartAssessmentAsync(
                AssessmentTitleBox.Text,
                duration,
                drafts);
            Refresh();
        }
        catch (Exception exception)
        {
            ShowError(exception);
        }
    }

    private async void EndLesson_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (MessageBox.Show(
                "Akhiri lesson aktif untuk semua target?",
                "Akhiri Lesson",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question,
                MessageBoxResult.No) != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            await _lessons.EndLessonAsync();
            Refresh();
        }
        catch (Exception exception)
        {
            ShowError(exception);
        }
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) =>
        Refresh();

    private void Refresh()
    {
        var active = _lessons.ActiveLesson;
        StatusText.Text = active is null
            ? "Belum ada lesson aktif."
            : $"{active.RoomName} ? {active.LessonTitle} ? " +
              $"{active.Phase} ? ID {active.LessonId}";
        RegistrationGrid.ItemsSource = active is null
            ? Array.Empty<object>()
            : _lessons.Registrations
                .Where(item => item.LessonId == active.LessonId)
                .ToArray();
        ResultGrid.ItemsSource = active is null
            ? _lessons.Results.Take(200).ToArray()
            : _lessons.Results
                .Where(item => item.LessonId == active.LessonId)
                .ToArray();
    }

    private static AssessmentQuestionDraft[] ParseQuestions(
        string raw)
    {
        var drafts = new List<AssessmentQuestionDraft>();
        foreach (var line in raw.Split(
                     new[] { '\r', '\n' },
                     StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split('|')
                .Select(part => part.Trim())
                .ToArray();
            if (parts.Length < 4
                || !int.TryParse(parts[^1], out var answerNumber))
            {
                throw new ArgumentException(
                    $"Format soal tidak valid: {line}");
            }

            var choices = parts[1..^1];
            drafts.Add(
                new AssessmentQuestionDraft(
                    parts[0],
                    choices,
                    answerNumber - 1));
        }

        if (drafts.Count == 0)
            throw new ArgumentException("Masukkan minimal satu soal.");
        return drafts.ToArray();
    }

    private static void ShowError(Exception exception) =>
        MessageBox.Show(
            exception.GetBaseException().Message,
            "Operasi classroom gagal",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
}
