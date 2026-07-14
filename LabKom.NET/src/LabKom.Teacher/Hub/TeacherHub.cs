using LabKom.Shared.Contracts;
using LabKom.Shared.Hub;
using LabKom.Teacher.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace LabKom.Teacher.Hub;

/// <summary>
/// SignalR hub yang di-host oleh Teacher Console.
/// Method-method di sini dipanggil oleh Student Agent atau Overlay (client → server).
/// </summary>
public class TeacherHub : Microsoft.AspNetCore.SignalR.Hub
{
    private readonly PresenceRegistry _registry;
    private readonly ActivityFeed _feed;
    private readonly ILogger<TeacherHub> _logger;
    private readonly string _sharedSecret;

    public TeacherHub(PresenceRegistry registry, ActivityFeed feed, ILogger<TeacherHub> logger, IConfiguration configuration)
    {
        _registry = registry;
        _feed = feed;
        _logger = logger;
        _sharedSecret = Environment.GetEnvironmentVariable("LABKOM_SHARED_SECRET")
                        ?? configuration["Teacher:SharedSecret"]
                        ?? string.Empty;
    }

    public override async Task OnConnectedAsync()
    {
        var request = Context.GetHttpContext()?.Request;
        var suppliedSecret = request?.Query[HubSecurity.QueryKey].ToString();
        var role = request?.Query[HubRoutes.Roles.Key].ToString();
        var pc = request?.Query["pc"].ToString();

        if (!HubSecurity.IsValidSecret(_sharedSecret, suppliedSecret)
            || (role != HubRoutes.Roles.Agent && role != HubRoutes.Roles.Overlay)
            || string.IsNullOrWhiteSpace(pc)
            || pc.Length > 63)
        {
            _logger.LogWarning("Koneksi Hub ditolak dari {RemoteIp}", request?.HttpContext.Connection.RemoteIpAddress);
            Context.Abort();
            return;
        }

        Context.Items["role"] = role;
        Context.Items["pc"] = pc;
        await Groups.AddToGroupAsync(Context.ConnectionId, GroupForPc(pc));
        await base.OnConnectedAsync();
    }
    public Task Hello(StudentPresence presence)
    {
        if (!IsAgentFor(presence.PcName)) return Task.CompletedTask;
        _registry.Upsert(presence, Context.ConnectionId);
        _logger.LogInformation("Hello dari {PcName} ({Ip})", presence.PcName, presence.IpAddress);

        // Daftarkan agent ke group PC supaya kita bisa target perintah.
        return Groups.AddToGroupAsync(Context.ConnectionId, GroupForPc(presence.PcName));
    }

    public Task Heartbeat(StudentPresence presence)
    {
        if (!IsAgentFor(presence.PcName)) return Task.CompletedTask;
        _registry.Upsert(presence, Context.ConnectionId);
        return Task.CompletedTask;
    }

    public Task ReportStatus(StudentStatus status)
    {
        if (!IsAgent()) return Task.CompletedTask;
        _registry.UpdateStatus(Context.ConnectionId, status);
        return Task.CompletedTask;
    }

    public Task SendChatToTeacher(ChatMessage message)
    {
        if (!IsAgentFor(message.FromPcName)) return Task.CompletedTask;
        _registry.RecordChat(message);
        return Task.CompletedTask;
    }

    public Task PushScreenFrame(ScreenFrame frame)
    {
        if (!IsAgentFor(frame.PcName)) return Task.CompletedTask;
        _registry.UpdateFrame(frame);
        return Task.CompletedTask;
    }

    public Task PushActivityRecord(ActivityRecord record)
    {
        if (!IsAgentFor(record.PcName)) return Task.CompletedTask;
        _feed.Push(record);
        return Task.CompletedTask;
    }

    public Task ReportFileProgress(FileDistributionProgress progress)
    {
        if (!IsAgentFor(progress.PcName)) return Task.CompletedTask;
        _feed.PushFileProgress(progress);
        return Task.CompletedTask;
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        if (!IsOverlay())
        {
            _registry.MarkDisconnected(Context.ConnectionId);
        }
        return base.OnDisconnectedAsync(exception);
    }

    public static string GroupForPc(string pcName) => $"pc:{pcName.ToLowerInvariant()}";

    private bool IsAgent() =>
        Context.Items.TryGetValue("role", out var role) && (string?)role == HubRoutes.Roles.Agent;

    private bool IsAgentFor(string? pcName) =>
        IsAgent()
        && Context.Items.TryGetValue("pc", out var pc)
        && string.Equals((string?)pc, pcName, StringComparison.OrdinalIgnoreCase);

    private bool IsOverlay() =>
        Context.Items.TryGetValue("role", out var v) && (string?)v == HubRoutes.Roles.Overlay;
}
