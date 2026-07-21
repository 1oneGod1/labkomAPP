using System.Windows;
using LabKom.Teacher.Services;
using Microsoft.Win32;

namespace LabKom.Teacher;

public partial class TechnicianConsoleWindow : Window
{
    private readonly TechnicianConsoleService _service;
    private TechnicianSummary? _summary;

    public TechnicianConsoleWindow(TechnicianConsoleService service)
    {
        _service = service;
        InitializeComponent();
        Refresh();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) =>
        Refresh();

    private void Refresh()
    {
        try
        {
            _summary = _service.Snapshot();
            DeviceGrid.ItemsSource = _summary.Devices;
            SummaryText.Text =
                $"Versi {_summary.Version} ? Classroom " +
                $"{_summary.ClassroomName} ({_summary.ClassroomId}) ? " +
                $"Key v{_summary.KeyVersion}, previous " +
                $"{_summary.AcceptedPreviousKeyVersions} ? " +
                $"Audit integrity: " +
                $"{(_summary.AuditIntegrityValid ? "VALID" : "INVALID")}";
        }
        catch (Exception exception)
        {
            ShowError(exception);
            Close();
        }
    }

    private async void RefreshStream_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (DeviceGrid.SelectedItem is not TechnicianDeviceRow row)
        {
            SelectDevice();
            return;
        }

        try
        {
            await _service.RefreshStreamAsync(row.PcName);
            Refresh();
        }
        catch (Exception exception)
        {
            ShowError(exception);
        }
    }

    private async void EmergencyUnlock_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (DeviceGrid.SelectedItem is not TechnicianDeviceRow row)
        {
            SelectDevice();
            return;
        }

        if (MessageBox.Show(
                $"Kirim emergency unlock ke {row.PcName}?",
                "Emergency Unlock",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No) != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            await _service.EmergencyUnlockAsync(row.PcName);
            Refresh();
        }
        catch (Exception exception)
        {
            ShowError(exception);
        }
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_summary is null) return;
        var dialog = new SaveFileDialog
        {
            Title = "Export diagnostik Technician Console",
            Filter = "CSV (*.csv)|*.csv",
            FileName =
                $"LabKom-Technician-{DateTime.Now:yyyyMMdd-HHmmss}.csv",
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            _service.ExportCsv(dialog.FileName, _summary.Devices);
        }
        catch (Exception exception)
        {
            ShowError(exception);
        }
    }

    private static void SelectDevice() =>
        MessageBox.Show(
            "Pilih satu perangkat terlebih dahulu.",
            "Perangkat belum dipilih",
            MessageBoxButton.OK,
            MessageBoxImage.Information);

    private static void ShowError(Exception exception) =>
        MessageBox.Show(
            exception.GetBaseException().Message,
            "Technician Console",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
}
