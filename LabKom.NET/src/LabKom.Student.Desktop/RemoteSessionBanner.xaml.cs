using System.Windows;
using LabKom.Shared.Contracts;

namespace LabKom.Student.Desktop;

public partial class RemoteSessionBanner : Window
{
    public RemoteSessionBanner()
    {
        InitializeComponent();
        Loaded += (_, _) => MoveToTopRight();
    }

    public event EventHandler? ReleaseRequested;

    public void SetSession(RemoteSessionCommand session)
    {
        TitleText.Text = session.Mode == RemoteSessionMode.Control
            ? "Guru mengontrol PC ini"
            : "Guru melihat layar PC ini";
        DetailText.Text = session.Mode == RemoteSessionMode.Control
            ? "Remote control aktif - Ctrl+Alt+Q untuk melepas"
            : "View-only aktif - Ctrl+Alt+Q untuk mengakhiri";
        MoveToTopRight();
    }

    private void MoveToTopRight()
    {
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - ActualWidth - 16;
        Top = workArea.Top + 16;
    }

    private void Release_Click(object sender, RoutedEventArgs e) =>
        ReleaseRequested?.Invoke(this, EventArgs.Empty);
}
