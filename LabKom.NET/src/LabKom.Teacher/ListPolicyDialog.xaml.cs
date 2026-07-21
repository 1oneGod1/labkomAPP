using System.Windows;

namespace LabKom.Teacher;

public partial class ListPolicyDialog : Window
{
    private ListPolicyDialog(
        string title,
        string instructions,
        string example,
        IEnumerable<string> initialValues)
    {
        InitializeComponent();
        Title = $"{title} - LabKom";
        HeadingText.Text = title;
        InstructionText.Text = instructions;
        ExampleText.Text = example;
        ValuesText.Text = string.Join(
            Environment.NewLine,
            initialValues);
        ValuesText.Focus();
        ValuesText.CaretIndex = ValuesText.Text.Length;
    }

    public IReadOnlyList<string>? Values { get; private set; }

    public static IReadOnlyList<string>? ShowEditor(
        string title,
        string instructions,
        string example,
        IEnumerable<string> initialValues)
    {
        var dialog = new ListPolicyDialog(
            title,
            instructions,
            example,
            initialValues)
        {
            Owner = Application.Current.MainWindow,
        };

        return dialog.ShowDialog() == true
            ? dialog.Values
            : null;
    }

    private void ApplyButton_OnClick(object sender, RoutedEventArgs e)
    {
        var values = ValuesText.Text
            .Split(
                new[] { Environment.NewLine, ",", ";" },
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (values.Length == 0)
        {
            MessageBox.Show(
                "Masukkan minimal satu nilai.",
                Title,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        Values = values;
        DialogResult = true;
    }
}