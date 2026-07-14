using LabKom.Shared.Contracts;
using LabKom.Shared.Hub;
using LabKom.Teacher.Hub;
using Microsoft.AspNetCore.SignalR;

namespace LabKom.Teacher.Services;

/// <summary>
/// Membantu UI Teacher mengirim perintah ke satu PC tertentu atau semua PC
/// melalui SignalR Hub. Pakai group-per-PC yang dibuat saat OnConnectedAsync.
/// </summary>
public class RemoteCommandService
{
    private readonly HubContextHolder _holder;
    private readonly PresenceRegistry _registry;

    public RemoteCommandService(HubContextHolder holder, PresenceRegistry registry)
    {
        _holder = holder;
        _registry = registry;
    }

    private IHubContext<TeacherHub> Hub =>
        _holder.HubContext ?? throw new InvalidOperationException("Hub belum siap.");

    public Task LockAsync(string? pcName, string message = "Mohon perhatian ke instruktur") =>
        SendAttention(pcName, enabled: true, message);

    public Task UnlockAsync(string? pcName) =>
        SendAttention(pcName, enabled: false, message: "");

    public Task ShutdownAsync(string? pcName, int delaySeconds = 0) =>
        SendPower(pcName, PowerCommand.Shutdown(delaySeconds, "Diperintahkan oleh guru"));

    public Task RestartAsync(string? pcName, int delaySeconds = 0) =>
        SendPower(pcName, PowerCommand.Restart(delaySeconds, "Diperintahkan oleh guru"));

    public Task SetCaptureProfileAsync(string pcName, CaptureProfile profile)
    {
        var group = TeacherHub.GroupForPc(pcName);
        return Hub.Clients.Group(group)
            .SendAsync(HubRoutes.Methods.ReceiveCaptureProfile, new CaptureProfileCommand(profile));
    }

    public Task BroadcastChatAsync(string body, string from = "Guru")
    {
        var msg = new ChatMessage(
            Guid.NewGuid().ToString("N"),
            ChatDirection.Broadcast,
            from,
            null,
            body,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        return Hub.Clients.All.SendAsync(HubRoutes.Methods.ReceiveChat, msg);
    }

    private Task SendAttention(string? pcName, bool enabled, string message)
    {
        var cmd = AttentionCommand.For(pcName, enabled, message);
        if (string.IsNullOrEmpty(pcName))
        {
            return Hub.Clients.All.SendAsync(HubRoutes.Methods.ReceiveAttention, cmd);
        }
        return Hub.Clients.Group(TeacherHub.GroupForPc(pcName))
            .SendAsync(HubRoutes.Methods.ReceiveAttention, cmd);
    }

    private Task SendPower(string? pcName, PowerCommand command)
    {
        if (string.IsNullOrEmpty(pcName))
        {
            return Hub.Clients.All.SendAsync(HubRoutes.Methods.ReceivePowerCommand, command);
        }
        return Hub.Clients.Group(TeacherHub.GroupForPc(pcName))
            .SendAsync(HubRoutes.Methods.ReceivePowerCommand, command);
    }
}
