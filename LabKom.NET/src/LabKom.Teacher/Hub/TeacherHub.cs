using LabKom.Shared.Contracts;
using LabKom.Shared.Hub;
using LabKom.Teacher.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace LabKom.Teacher.Hub;

/// <summary>
/// SignalR control plane hosted by Teacher. Every connection is bound to one
/// validated PC identity and one role: privileged Agent or interactive Desktop.
/// </summary>
public sealed class TeacherHub : Microsoft.AspNetCore.SignalR.Hub
{
    private const string RoleItem = "role";
    private const string PcItem = "pc";

    private readonly PresenceRegistry _registry;
    private readonly ActivityFeed _feed;
    private readonly AttentionStateStore _attentionState;
    private readonly TeacherScreenBroadcaster _screenBroadcaster;
    private readonly ILogger<TeacherHub> _logger;
    private readonly string _sharedSecret;

    public TeacherHub(
        PresenceRegistry registry,
        ActivityFeed feed,
        AttentionStateStore attentionState,
        TeacherScreenBroadcaster screenBroadcaster,
        ILogger<TeacherHub> logger,
        IConfiguration configuration)
    {
        _registry = registry;
        _feed = feed;
        _attentionState = attentionState;
        _screenBroadcaster = screenBroadcaster;
        _logger = logger;
        _sharedSecret = Environment.GetEnvironmentVariable("LABKOM_SHARED_SECRET")
                        ?? configuration["Teacher:SharedSecret"]
                        ?? string.Empty;
    }

    public override async Task OnConnectedAsync()
    {
        var request = Context.GetHttpContext()?.Request;
        var suppliedSecret = request?.Headers[HubSecurity.HeaderName].ToString();
        var role = request?.Query[HubRoutes.Roles.Key].ToString();
        var pcName = request?.Query["pc"].ToString().Trim();

        if (!HubSecurity.IsValidSecret(_sharedSecret, suppliedSecret)
            || !HubRoutes.Roles.IsKnown(role)
            || !HubSecurity.IsValidPcName(pcName))
        {
            _logger.LogWarning("Koneksi Hub ditolak dari {RemoteIp}", request?.HttpContext.Connection.RemoteIpAddress);
            Context.Abort();
            return;
        }

        Context.Items[RoleItem] = role!;
        Context.Items[PcItem] = pcName!;
        await Groups.AddToGroupAsync(Context.ConnectionId, HubRoutes.Groups.ForRole(role!));
        await Groups.AddToGroupAsync(Context.ConnectionId, HubRoutes.Groups.ForPcRole(pcName!, role!));
        var isDesktop = string.Equals(role, HubRoutes.Roles.Desktop, StringComparison.Ordinal);
        if (isDesktop)
        {
            _registry.RegisterDesktop(pcName!, Context.ConnectionId);
        }

        await base.OnConnectedAsync();

        if (isDesktop && _attentionState.BuildReplayFor(pcName!) is { } attentionReplay)
        {
            await Clients.Client(Context.ConnectionId)
                .SendAsync(HubRoutes.Methods.ReceiveAttention, attentionReplay);
        }

        if (isDesktop && _screenBroadcaster.BuildReplayFor(pcName!) is { } broadcastReplay)
        {
            await Clients.Client(Context.ConnectionId)
                .SendAsync(
                    HubRoutes.Methods.ReceiveTeacherBroadcastSignal,
                    broadcastReplay);
        }
    }

    public Task Hello(StudentPresence presence)
    {
        if (!IsRoleFor(HubRoutes.Roles.Agent, presence.PcName)) return Task.CompletedTask;
        _registry.Upsert(presence, Context.ConnectionId);
        _logger.LogInformation("Hello dari {PcName} ({Ip})", presence.PcName, presence.IpAddress);
        return Task.CompletedTask;
    }

    public Task Heartbeat(StudentPresence presence)
    {
        if (!IsRoleFor(HubRoutes.Roles.Agent, presence.PcName)) return Task.CompletedTask;
        _registry.Upsert(presence, Context.ConnectionId);
        return Task.CompletedTask;
    }

    public Task ReportStatus(StudentStatus status)
    {
        if (!IsRole(HubRoutes.Roles.Agent) || !Enum.IsDefined(status)) return Task.CompletedTask;
        _registry.UpdateStatus(Context.ConnectionId, status);
        return Task.CompletedTask;
    }

    public Task SendChatToTeacher(ChatMessage message)
    {
        var pcName = ConnectedPc;
        if (!IsRole(HubRoutes.Roles.Desktop)
            || pcName is null
            || !ContractValidation.IsValidChat(
                message,
                pcName,
                ChatDirection.StudentToTeacher))
        {
            return Task.CompletedTask;
        }

        _registry.RecordChat(message);
        return Task.CompletedTask;
    }

    public Task PushScreenFrame(ScreenFrame frame)
    {
        var pcName = ConnectedPc;
        if (!IsRole(HubRoutes.Roles.Desktop)
            || pcName is null
            || !ContractValidation.IsValidScreenFrame(frame, pcName))
        {
            return Task.CompletedTask;
        }

        _registry.UpdateFrame(frame, Context.ConnectionId);
        return Task.CompletedTask;
    }

    public Task PushMonitorInventory(MonitorInventory inventory)
    {
        var pcName = ConnectedPc;
        if (!IsRole(HubRoutes.Roles.Desktop)
            || pcName is null
            || !ContractValidation.IsValidMonitorInventory(inventory, pcName))
        {
            return Task.CompletedTask;
        }

        _registry.UpdateMonitorInventory(inventory, Context.ConnectionId);
        return Task.CompletedTask;
    }

    public Task PushActivityRecord(ActivityRecord record)
    {
        var pcName = ConnectedPc;
        if (!IsRole(HubRoutes.Roles.Desktop)
            || pcName is null
            || !ContractValidation.IsValidActivity(record, pcName))
        {
            return Task.CompletedTask;
        }

        _feed.Push(record);
        return Task.CompletedTask;
    }

    public Task ReportFileProgress(FileDistributionProgress progress)
    {
        var pcName = ConnectedPc;
        if (!IsRole(HubRoutes.Roles.Desktop)
            || pcName is null
            || !ContractValidation.IsValidFileProgress(progress, pcName))
        {
            return Task.CompletedTask;
        }

        _feed.PushFileProgress(progress);
        return Task.CompletedTask;
    }

    public Task ReportCommandResult(CommandResult result)
    {
        var pcName = ConnectedPc;
        if (pcName is null
            || !ContractValidation.IsValidCommandResult(result, pcName)
            || !RoleCanReport(result.Kind))
        {
            return Task.CompletedTask;
        }

        _feed.PushCommandResult(result);
        return Task.CompletedTask;
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        if (IsRole(HubRoutes.Roles.Agent))
        {
            _registry.MarkAgentDisconnected(Context.ConnectionId);
        }
        else if (IsRole(HubRoutes.Roles.Desktop))
        {
            _registry.MarkDesktopDisconnected(Context.ConnectionId);
        }

        return base.OnDisconnectedAsync(exception);
    }

    private string? ConnectedPc =>
        Context.Items.TryGetValue(PcItem, out var value) ? value as string : null;

    private bool IsRole(string expectedRole) =>
        Context.Items.TryGetValue(RoleItem, out var role)
        && string.Equals(role as string, expectedRole, StringComparison.Ordinal);

    private bool IsRoleFor(string expectedRole, string? pcName) =>
        IsRole(expectedRole)
        && string.Equals(ConnectedPc, pcName, StringComparison.OrdinalIgnoreCase);

    private bool RoleCanReport(RemoteCommandKind kind) => kind switch
    {
        RemoteCommandKind.Attention => IsRole(HubRoutes.Roles.Desktop),
        RemoteCommandKind.Power or RemoteCommandKind.WebFilter or RemoteCommandKind.AppBlock =>
            IsRole(HubRoutes.Roles.Agent),
        _ => false,
    };
}