using LabKom.Shared.Contracts;

namespace LabKom.Shared.Hub;

/// <summary>
/// Strongly-typed kontrak SignalR untuk method yang dipanggil server (Hub) → student client.
/// Pakai di Student Agent saat subscribe ke event hub.
/// </summary>
public interface IStudentHubClient
{
    Task ReceiveAttention(AttentionCommand command);
    Task ReceiveChat(ChatMessage message);
    Task ReceivePowerCommand(PowerCommand command);
    Task ReceiveCaptureProfile(CaptureProfileCommand command);
}
