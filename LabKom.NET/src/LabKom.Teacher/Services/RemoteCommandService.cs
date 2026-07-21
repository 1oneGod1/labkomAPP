using LabKom.Shared.Contracts;
using LabKom.Shared.Hub;
using LabKom.Shared.Security;
using LabKom.Teacher.Hub;
using Microsoft.AspNetCore.SignalR;

namespace LabKom.Teacher.Services;

/// <summary>Routes each command only to the process role that can execute it.</summary>
public sealed class RemoteCommandService
{
    private readonly HubContextHolder _holder;
    private readonly AttentionStateStore _attentionState;
    private readonly ClassPolicyStateStore _policyState;
    private readonly TeacherAuthorizationService _authorization;

    public RemoteCommandService(
        HubContextHolder holder,
        AttentionStateStore attentionState,
        ClassPolicyStateStore policyState,
        TeacherAuthorizationService authorization)
    {
        _holder = holder;
        _attentionState = attentionState;
        _policyState = policyState;
        _authorization = authorization;
    }

    private IHubContext<TeacherHub> Hub =>
        _holder.HubContext ?? throw new InvalidOperationException("Hub belum siap.");

    public Task LockAsync(string? pcName, string message = "Mohon perhatian ke instruktur") =>
        Authorized(
            TeacherPermission.ManageAttention,
            "attention.lock",
            pcName,
            () => SendAttention(pcName, enabled: true, message));

    public Task UnlockAsync(string? pcName) =>
        Authorized(
            TeacherPermission.ManageAttention,
            "attention.unlock",
            pcName,
            () => SendAttention(pcName, enabled: false, message: string.Empty));

    public Task ShutdownAsync(string? pcName, int delaySeconds = 0) =>
        Authorized(
            TeacherPermission.ManagePower,
            "power.shutdown",
            pcName,
            () => SendPower(pcName, PowerCommand.Shutdown(delaySeconds, "Diperintahkan oleh guru")));

    public Task RestartAsync(string? pcName, int delaySeconds = 0) =>
        Authorized(
            TeacherPermission.ManagePower,
            "power.restart",
            pcName,
            () => SendPower(pcName, PowerCommand.Restart(delaySeconds, "Diperintahkan oleh guru")));

    public Task LogOffAsync(string? pcName) =>
        Authorized(
            TeacherPermission.ManagePower,
            "power.logoff",
            pcName,
            () => SendPower(pcName, PowerCommand.LogOff()));

    public Task SetCaptureProfileAsync(
        string pcName,
        CaptureProfile profile,
        string? monitorId = null)
    {
        _authorization.Demand(TeacherPermission.ViewClassroom, "capture.profile", pcName);
        var command = new CaptureProfileCommand(profile, monitorId);
        if (!ContractValidation.IsValidCaptureProfileCommand(command))
        {
            throw new ArgumentException("Capture profile atau monitor tidak valid.", nameof(monitorId));
        }

        return Hub.Clients
            .Group(HubRoutes.Groups.ForPcRole(pcName, HubRoutes.Roles.Desktop))
            .SendAsync(HubRoutes.Methods.ReceiveCaptureProfile, command);
    }

    public Task BroadcastChatAsync(string body, string from = "Guru") =>
        SendChatAsync(targetPcName: null, body, from);

    public Task SendChatAsync(string? targetPcName, string body, string from = "Guru")
    {
        _authorization.Demand(TeacherPermission.SendMessage, "chat.send", targetPcName);
        var message = new ChatMessage(
            Guid.NewGuid().ToString("N"),
            targetPcName is null
                ? ChatDirection.Broadcast
                : ChatDirection.TeacherToStudent,
            from,
            targetPcName,
            body?.Trim() ?? string.Empty,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        if (!ContractValidation.IsValidChat(message))
        {
            throw new ArgumentException("Pesan chat tidak valid.", nameof(body));
        }

        var group = targetPcName is null
            ? HubRoutes.Groups.ForRole(HubRoutes.Roles.Desktop)
            : HubRoutes.Groups.ForPcRole(targetPcName, HubRoutes.Roles.Desktop);
        return Hub.Clients.Group(group)
            .SendAsync(HubRoutes.Methods.ReceiveChat, message);
    }

    public Task ApplyWebFilterAsync(IEnumerable<string> domains)
    {
        _authorization.Demand(TeacherPermission.ManagePolicies, "policy.web.apply", null);
        var policy = WebFilterPolicy.Blacklist(domains);
        if (!ContractValidation.IsValidWebFilterPolicy(policy))
        {
            throw new ArgumentException("Daftar domain blokir tidak valid.", nameof(domains));
        }

        _policyState.Apply(policy);
        return SendPolicy(HubRoutes.Methods.ReceiveWebFilterPolicy, policy);
    }

    public Task DisableWebFilterAsync()
    {
        _authorization.Demand(TeacherPermission.ManagePolicies, "policy.web.disable", null);
        var policy = WebFilterPolicy.Disabled;
        _policyState.Apply(policy);
        return SendPolicy(HubRoutes.Methods.ReceiveWebFilterPolicy, policy);
    }

    public Task ApplyAppBlockAsync(IEnumerable<string> processNames)
    {
        _authorization.Demand(TeacherPermission.ManagePolicies, "policy.app.apply", null);
        var policy = AppBlockPolicy.Block(processNames);
        if (!ContractValidation.IsValidAppBlockPolicy(policy))
        {
            throw new ArgumentException("Daftar aplikasi blokir tidak valid.", nameof(processNames));
        }

        _policyState.Apply(policy);
        return SendPolicy(HubRoutes.Methods.ReceiveAppBlockPolicy, policy);
    }

    public Task DisableAppBlockAsync()
    {
        _authorization.Demand(TeacherPermission.ManagePolicies, "policy.app.disable", null);
        var policy = AppBlockPolicy.Disabled;
        _policyState.Apply(policy);
        return SendPolicy(HubRoutes.Methods.ReceiveAppBlockPolicy, policy);
    }

    private Task Authorized(
        TeacherPermission permission,
        string action,
        string? target,
        Func<Task> operation)
    {
        _authorization.Demand(permission, action, target);
        return operation();
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

    private Task SendPolicy<TPolicy>(string method, TPolicy policy) =>
        Hub.Clients.Group(HubRoutes.Groups.ForRole(HubRoutes.Roles.Agent))
            .SendAsync(method, policy);
}