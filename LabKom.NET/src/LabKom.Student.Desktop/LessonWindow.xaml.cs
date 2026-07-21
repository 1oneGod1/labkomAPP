using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using LabKom.Shared.Contracts;

namespace LabKom.Student.Desktop;

public partial class LessonWindow : Window
{
    private readonly string _pcName;
    private readonly Func<StudentRegistrationSubmission, Task> _register;
    private readonly Func<AssessmentSubmission, Task> _submit;
    private readonly Dictionary<string, List<RadioButton>> _choices =
        new(StringComparer.Ordinal);
    private readonly DispatcherTimer _timer;
    private LessonSnapshot _lesson;
    private bool _assessmentSubmitted;

    public LessonWindow(
        string pcName,
        LessonSnapshot lesson,
        Func<StudentRegistrationSubmission, Task> register,
        Func<AssessmentSubmission, Task> submit)
    {
        _pcName = pcName;
        _lesson = lesson;
        _register = register;
        _submit = submit;
        InitializeComponent();
        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1),
        };
        _timer.Tick += Timer_Tick;
        _timer.Start();
        Apply(lesson);
        Closed += (_, _) => _timer.Stop();
    }

    public void Apply(LessonSnapshot lesson)
    {
        _lesson = lesson;
        RoomText.Text = lesson.RoomName;
        LessonTitleText.Text = lesson.LessonTitle;
        PhaseText.Text = lesson.Phase switch
        {
            LessonPhase.Registration => "Register siswa",
            LessonPhase.Teaching => "Lesson berlangsung",
            LessonPhase.Assessment => "Assessment",
            LessonPhase.Ended => "Lesson selesai",
            _ => lesson.Phase.ToString(),
        };

        if (lesson.Phase == LessonPhase.Ended)
        {
            SubmitStatusText.Text = "Lesson telah selesai.";
            SubmitAssessmentButton.IsEnabled = false;
            RegisterButton.IsEnabled = false;
            return;
        }

        BuildAssessment(lesson.Assessment);
        UpdateTimer();
    }

    private void BuildAssessment(AssessmentDelivery? assessment)
    {
        QuestionsPanel.Children.Clear();
        _choices.Clear();
        _assessmentSubmitted = false;
        SubmitStatusText.Text = string.Empty;
        if (assessment is null)
        {
            AssessmentTitleText.Text = string.Empty;
            InformationText.Text = _lesson.Phase == LessonPhase.Registration
                ? "Isi identitas lalu tekan Register."
                : "Lesson sedang berlangsung. Ikuti arahan guru.";
            SubmitAssessmentButton.Visibility = Visibility.Collapsed;
            return;
        }

        InformationText.Text = string.Empty;
        AssessmentTitleText.Text =
            $"{assessment.Title} ? {assessment.DurationMinutes} menit";
        foreach (var question in assessment.Questions)
        {
            var box = new GroupBox
            {
                Header = question.Prompt,
                Margin = new Thickness(0, 0, 0, 13),
                Padding = new Thickness(12),
                Foreground = System.Windows.Media.Brushes.White,
                BorderBrush = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(51, 65, 85)),
            };
            var options = new StackPanel();
            var buttons = new List<RadioButton>();
            for (var index = 0; index < question.Choices.Count; index++)
            {
                var button = new RadioButton
                {
                    Content = question.Choices[index],
                    Tag = index,
                    GroupName = question.QuestionId,
                    Margin = new Thickness(0, 5, 0, 5),
                };
                buttons.Add(button);
                options.Children.Add(button);
            }
            _choices[question.QuestionId] = buttons;
            box.Content = options;
            QuestionsPanel.Children.Add(box);
        }

        SubmitAssessmentButton.Visibility = Visibility.Visible;
        SubmitAssessmentButton.IsEnabled = true;
    }

    private async void Register_Click(object sender, RoutedEventArgs e)
    {
        var submission = new StudentRegistrationSubmission(
            _lesson.LessonId,
            _pcName,
            StudentCodeBox.Text.Trim(),
            FullNameBox.Text.Trim(),
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        if (!ContractValidation.IsValidStudentRegistration(
                submission,
                _pcName))
        {
            MessageBox.Show(
                "NIS/ID dan nama lengkap wajib diisi.",
                "Register belum lengkap",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        RegisterButton.IsEnabled = false;
        try
        {
            await _register(submission);
            SubmitStatusText.Text = "Register berhasil dikirim.";
        }
        catch
        {
            RegisterButton.IsEnabled = true;
            SubmitStatusText.Text = "Register gagal dikirim.";
        }
    }

    private async void SubmitAssessment_Click(
        object sender,
        RoutedEventArgs e) =>
        await SubmitAssessmentAsync(autoSubmit: false);

    private async Task SubmitAssessmentAsync(bool autoSubmit)
    {
        if (_assessmentSubmitted || _lesson.Assessment is null) return;
        var answers = new List<AssessmentAnswer>();
        foreach (var question in _lesson.Assessment.Questions)
        {
            var selected = _choices[question.QuestionId]
                .FirstOrDefault(button => button.IsChecked == true);
            if (selected is null)
            {
                if (!autoSubmit)
                {
                    MessageBox.Show(
                        "Semua soal harus dijawab sebelum dikirim.",
                        "Jawaban belum lengkap",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }
                answers.Add(new AssessmentAnswer(question.QuestionId, 0));
            }
            else
            {
                answers.Add(
                    new AssessmentAnswer(
                        question.QuestionId,
                        (int)selected.Tag));
            }
        }

        var submission = new AssessmentSubmission(
            _lesson.Assessment.AssessmentId,
            _lesson.LessonId,
            _pcName,
            StudentCodeBox.Text.Trim(),
            answers,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        if (!ContractValidation.IsValidAssessmentSubmission(
                submission,
                _pcName))
        {
            MessageBox.Show(
                "Register NIS/ID sebelum mengirim assessment.",
                "Identitas belum lengkap",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        SubmitAssessmentButton.IsEnabled = false;
        try
        {
            await _submit(submission);
            _assessmentSubmitted = true;
            SubmitStatusText.Text = autoSubmit
                ? "Waktu habis; jawaban dikirim otomatis."
                : "Jawaban berhasil dikirim.";
        }
        catch
        {
            SubmitAssessmentButton.IsEnabled = true;
            SubmitStatusText.Text = "Jawaban gagal dikirim.";
        }
    }

    private async void Timer_Tick(object? sender, EventArgs e)
    {
        UpdateTimer();
        if (_lesson.Phase == LessonPhase.Assessment
            && !_assessmentSubmitted
            && DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                >= _lesson.ExpiresAtUnixMs)
        {
            await SubmitAssessmentAsync(autoSubmit: true);
        }
    }

    private void UpdateTimer()
    {
        var remaining = DateTimeOffset
            .FromUnixTimeMilliseconds(_lesson.ExpiresAtUnixMs)
            - DateTimeOffset.UtcNow;
        TimerText.Text = remaining <= TimeSpan.Zero
            ? "Waktu habis"
            : $"Sisa {remaining:hh\\:mm\\:ss}";
    }
}
