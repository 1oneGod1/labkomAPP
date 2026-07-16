using LabKom.Shared.Contracts;
using LabKom.Shared.Hub;
using LabKom.Teacher.Hub;
using Microsoft.AspNetCore.SignalR;

namespace LabKom.Teacher.Services;

/// <summary>Routes each command only to the process role that can execute it.</summary>
public sealed class RemoteCommandService
{
    private readonly HubContextHolder _holder;
    private readonly AttentionStateStore _attentionState;

    public RemoteCommandService(
        HubContextHolder holder,
        AttentionStateStore attentionState)
    {
        _holder = holder;
        _attentionState = attentionState;
    }

    private IHubContext<TeacherHub> Hub =>
        _holder.HubContext ?? throw new InvalidOperationException("Hub belum siap.");

    public Task LockAsync(string? pcName, string message = "Mohon perhatian ke instruktur") =>
        SendAttention(pcName, enabled: true, message);

    public Task UnlockAsync(string? pcName) =>
        SendAttention(pcName, enabled: false, message: string.Empty);

    public Task ShutdownAsync(string? pcName, int delaySeconds = 0) =>
        SendPower(pcName, PowerCommand.Shutdown(delaySeconds, "Diperintahkan oleh guru"));

    public Task RestartAsync(string? pcName, int delaySeconds = 0) =>
        SendPower(pcName, PowerCommand.Restart(delaySeconds, "Diperintahkan oleh guru"));

    public Task SetCaptureProfileAsync(
        string pcName,
        CaptureProfile profile,
        string? monitorId = null)
    {
        var command = new CaptureProfileCommand(profile, monitorId);
        if (!ContractValidation.IsValidCaptureProfileCommand(command))
        {
            throw new ArgumentException("Capture profile atau monitor tidak valid.", nameof(monitorId));
        }

        return Hub.Clients
            .Group(HubRoutes.Groups.ForPcRole(pcName, HubRoutes.Roles.Desktop))
            .SendAsync(HubRoutes.Methods.ReceiveCaptureProfile, command);
    }

    public Task BroadcastChatAsync(string body, string from = "Guru")
    {
        var message = new ChatMessage(
            Guid.NewGuid().ToString("N"),
            ChatDirection.Broadcast,
            from,
            null,
            body?.Trim() ?? string.Empty,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        if (!ContractValidation.IsValidChat(message))
        {
            throw new ArgumentException("Pesan chat tidak valid.", nameof(body));
        }

        return Hub.Clients
            .Group(HubRoutes.Groups.ForRole(HubRoutes.Roles.Desktop))
            .SendAsync(HubRoutes.Methods.ReceiveChat, message);
    }

    private Task SendAttention(string? pcName, bool enabled, string message)
    {
        var command = AttentionCommand.For(pcName, enabled, message);
        _attentionState.Apply(command);
        var group = string.IsNullOrWhiteSpace(pcName)
            ? HubRoutes.Groups.ForRole(HubRoutes.Roles.Desktop)
            : HubRoutes.Groups.ForPcRole(pcName, HubRoutes.Roles.Desktop);

        return Hub.Clients.Group(group)
            .SendAsync(HubRoutes.Methods.ReceiveAttention, command);
    }

    private Task SendPower(string? pcName, PowerCommand command)
    {
        var group = string.IsNullOrWhiteSpace(pcName)
            ? HubRoutes.Groups.ForRole(HubRoutes.Roles.Agent)
            : HubRoutes.Groups.ForPcRole(pcName, HubRoutes.Roles.Agent);

        return Hub.Clients.Group(group)
            .SendAsync(HubRoutes.Methods.ReceivePowerCommand, command);
    }
}