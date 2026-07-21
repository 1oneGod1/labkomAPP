using System.IO;
using System.Windows;
using System.Windows.Controls;
using LabKom.Shared.Contracts;

namespace LabKom.Teacher;

public partial class FileCollectDialog : Window
{
    public FileCollectSelection? Selection { get; private set; }

    public FileCollectDialog()
    {
        InitializeComponent();
    }

    private void Submit_Click(object sender, RoutedEventArgs e)
    {
        var rawRoot = (RootCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        var path = RelativePathBox.Text.Trim();
        if (!Enum.TryParse<FileCollectionRoot>(rawRoot, out var root)
            || string.IsNullOrWhiteSpace(path)
            || Path.IsPathRooted(path)
            || path.Contains('*')
            || path.Contains('?')
            || path.Split(
                    new[] { '/', '\\' },
                    StringSplitOptions.RemoveEmptyEntries)
                .Any(part => part is "." or ".."))
        {
            MessageBox.Show(
                "Masukkan satu path relatif yang valid, misalnya Tugas.docx atau KelasA\\Tugas.docx.",
                "Path tidak valid",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        Selection = new FileCollectSelection(root, path);
        DialogResult = true;
    }

    public static FileCollectSelection? Show(Window? owner)
    {
        var dialog = new FileCollectDialog { Owner = owner };
        return dialog.ShowDialog() == true ? dialog.Selection : null;
    }
}

public sealed record FileCollectSelection(
    FileCollectionRoot Root,
    string RelativePath);
