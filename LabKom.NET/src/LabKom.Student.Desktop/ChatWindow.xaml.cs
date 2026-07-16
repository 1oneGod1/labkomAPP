using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using LabKom.Shared.Contracts;

namespace LabKom.Student.Desktop;

public partial class ChatWindow : Window
{
    public ChatWindow()
    {
        InitializeComponent();
        DataContext = this;
    }

    public ObservableCollection<ChatLine> Messages { get; } = new();

    public event EventHandler<string>? MessageSubmitted;

    public void Append(ChatMessage message)
    {
        var sender = message.Direction == ChatDirection.StudentToTeacher
            ? "Anda"
            : string.IsNullOrWhiteSpace(message.FromPcName)
                ? "Instruktur"
                : message.FromPcName;
        var timestamp = DateTimeOffset.FromUnixTimeMilliseconds(message.TimestampUnixMs)
            .LocalDateTime
            .ToString("HH:mm");

        Messages.Add(new ChatLine(sender, message.Body, timestamp));
        while (Messages.Count > 200)
        {
            Messages.RemoveAt(0);
        }

        if (Messages.Count > 0)
        {
            MessageList.ScrollIntoView(Messages[^1]);
        }
    }

    private void SendButton_OnClick(object sender, RoutedEventArgs e) => SubmitReply();

    private void ReplyText_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) return;
        e.Handled = true;
        SubmitReply();
    }

    private void SubmitReply()
    {
        var body = ReplyText.Text.Trim();
        if (string.IsNullOrWhiteSpace(body)) return;

        ReplyText.Clear();
        MessageSubmitted?.Invoke(this, body);
    }
}

public sealed record ChatLine(string Sender, string Body, string Time);
