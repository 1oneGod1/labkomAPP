using LabKom.Shared.Contracts;
using LabKom.Shared.Hub;
using LabKom.Shared.Security;
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
    private const string DeviceItem = "device";
    private const string KeyVersionItem = "key-version";
    private const string LegacyItem = "legacy";
    private const string PendingRotationItem = "pending-key-rotation";

    private readonly PresenceRegistry _registry;
    private readonly ActivityFeed _feed;
    private readonly FileCollectionService _fileCollection;
    private readonly AttentionStateStore _attentionState;
    private readonly ClassPolicyStateStore _policyState;
    private readonly ClassroomSessionIdentity _sessionIdentity;
    private readonly ClassroomLessonService _lessons;
    private readonly TeacherScreenBroadcaster _screenBroadcaster;
    private readonly ILogger<TeacherHub> _logger;
    private readonly string _sharedSecret;

    private readonly string _classroomId;
    private readonly bool _allowLegacy;
    private readonly TeacherAuthorizationService _authorization;
    private readonly TelemetryRegistry _telemetry;
    public TeacherHub(
        PresenceRegistry registry,
        ActivityFeed feed,
        FileCollectionService fileCollection,
        AttentionStateStore attentionState,
        ClassPolicyStateStore policyState,
        ClassroomSessionIdentity sessionIdentity,
        ClassroomLessonService lessons,
        TeacherScreenBroadcaster screenBroadcaster,
        TeacherAuthorizationService authorization,
        TelemetryRegistry telemetry,
        ILogger<TeacherHub> logger,
        IConfiguration configuration)
    {
        _registry = registry;
        _feed = feed;
        _fileCollection = fileCollection;
        _attentionState = attentionState;
        _policyState = policyState;
        _sessionIdentity = sessionIdentity;
        _lessons = lessons;
        _screenBroadcaster = screenBroadcaster;
        _logger = logger;
        _sharedSecret = Environment.GetEnvironmentVariable("LABKOM_SHARED_SECRET")
                        ?? configuration["Teacher:SharedSecret"]
                        ?? string.Empty;
        _authorization = authorization;
        _telemetry = telemetry;
        _classroomId = configuration["Security:ClassroomId"] ?? string.Empty;
        _allowLegacy = configuration.GetValue(
            "Security:AllowLegacySharedSecret",
            true);
    }

    public override async Task OnConnectedAsync()
    {
        var request = Context.GetHttpContext()?.Request;
        var suppliedSecret = request?.Headers[HubSecurity.HeaderName].ToString();
        var deviceId = request?.Headers[HubSecurity.DeviceIdHeaderName].ToString();
        var rawKeyVersion = request?.Headers[HubSecurity.KeyVersionHeaderName].ToString();
        int? keyVersion = int.TryParse(
            rawKeyVersion,
            System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture,
            out var parsedVersion)
                ? parsedVersion
                : null;
        var role = request?.Query[HubRoutes.Roles.Key].ToString();
        var pcName = request?.Query["pc"].ToString().Trim();
        var policy = KeyRotationPolicyStore.ReadOrDefault();
        var authentication = Guid.TryParseExact(_classroomId, "N", out _)
            ? DeviceAuthentication.Validate(
                _sharedSecret,
                _classroomId,
                suppliedSecret,
                deviceId,
                pcName ?? string.Empty,
                keyVersion,
                policy.CurrentVersion,
                policy.AcceptedPreviousVersions,
                _allowLegacy)
            : _allowLegacy && HubSecurity.IsValidSecret(_sharedSecret, suppliedSecret)
                ? new DeviceAuthenticationResult(true, true, null, null, "legacy-no-classroom-id")
                : DeviceAuthenticationResult.Rejected("classroom-id-missing");

        if (!authentication.Success
            || !HubRoutes.Roles.IsKnown(role)
            || !HubSecurity.IsValidPcName(pcName))
        {
            _logger.LogWarning(
                "Koneksi Hub ditolak dari {RemoteIp}: {Reason}",
                request?.HttpContext.Connection.RemoteIpAddress,
                authentication.Reason);
            _ = TryAuditAuthentication(pcName, "rejected", authentication.Reason);
            Context.Abort();
            return;
        }

        if (!TryAuditAuthentication(
                pcName,
                "accepted",
                authentication.IsLegacy ? "legacy" : $"device:{authentication.DeviceId}:v{authentication.KeyVersion}"))
        {
            Context.Abort();
            return;
        }

        Context.Items[RoleItem] = role!;
        Context.Items[PcItem] = pcName!;
        Context.Items[DeviceItem] = authentication.DeviceId ?? "legacy";
        Context.Items[KeyVersionItem] = authentication.KeyVersion ?? 0;
        Context.Items[LegacyItem] = authentication.IsLegacy;
        await Groups.AddToGroupAsync(Context.ConnectionId, HubRoutes.Groups.ForRole(role!));
        await Groups.AddToGroupAsync(Context.ConnectionId, HubRoutes.Groups.ForPcRole(pcName!, role!));
        var isDesktop = string.Equals(role, HubRoutes.Roles.Desktop, StringComparison.Ordinal);
        var isAgent = string.Equals(role, HubRoutes.Roles.Agent, StringComparison.Ordinal);
        if (isDesktop)
        {
            _registry.RegisterDesktop(pcName!, Context.ConnectionId);
        }

        await base.OnConnectedAsync();

        if (isAgent)
        {
            await Clients.Client(Context.ConnectionId).SendAsync(
                HubRoutes.Methods.ReceiveWebFilterPolicy,
                _policyState.BuildWebReplay());
            await Clients.Client(Context.ConnectionId).SendAsync(
                HubRoutes.Methods.ReceiveAppBlockPolicy,
                _policyState.BuildAppReplay());
        }
        if (isDesktop)
        {
            var attentionReplay = _attentionState.BuildReplayFor(pcName!);
            var broadcastReplay = _screenBroadcaster.BuildReplayFor(pcName!);

            await Clients.Client(Context.ConnectionId).SendAsync(
                HubRoutes.Methods.ReceiveClassroomStateSnapshot,
                _sessionIdentity.CreateSnapshot(attentionReplay, broadcastReplay));

            // Replay lama dipertahankan agar Student versi sebelumnya tetap
            // menerima mode aktif yang sedang berlangsung.
            if (attentionReplay is not null)
            {
                await Clients.Client(Context.ConnectionId)
                    .SendAsync(HubRoutes.Methods.ReceiveAttention, attentionReplay);
            }

            if (broadcastReplay is not null)
            {
                await Clients.Client(Context.ConnectionId).SendAsync(
                    HubRoutes.Methods.ReceiveTeacherBroadcastSignal,
                    broadcastReplay);
            }

            var lessonReplay = _lessons.ReplayFor(pcName!);
            if (lessonReplay is not null)
            {
                await Clients.Client(Context.ConnectionId).SendAsync(
                    HubRoutes.Methods.ReceiveLessonSnapshot,
                    lessonReplay);
            }
        }
    }
    public async Task Hello(StudentPresence presence)
    {
        if (!IsRoleFor(HubRoutes.Roles.Agent, presence.PcName)) return;
        _registry.Upsert(presence, Context.ConnectionId);
        _logger.LogInformation("Hello dari {PcName} ({Ip})", presence.PcName, presence.IpAddress);
        await SendRotationNoticeIfNeeded();
    }

    public async Task Heartbeat(StudentPresence presence)
    {
        if (!IsRoleFor(HubRoutes.Roles.Agent, presence.PcName)) return;
        _registry.Upsert(presence, Context.ConnectionId);
        await SendRotationNoticeIfNeeded();
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
    public Task ReportRemoteSessionStatus(RemoteSessionStatus status)
    {
        var pcName = ConnectedPc;
        if (!IsRole(HubRoutes.Roles.Desktop)
            || pcName is null
            || !ContractValidation.IsValidRemoteSessionStatus(
                status,
                pcName))
        {
            return Task.CompletedTask;
        }

        _registry.RecordRemoteSessionStatus(status);
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

    public Task PushDeviceTelemetry(DeviceTelemetry telemetry)
    {
        var pcName = ConnectedPc;
        if (!IsRole(HubRoutes.Roles.Agent)
            || pcName is null
            || !ContractValidation.IsValidDeviceTelemetry(telemetry, pcName))
        {
            _telemetry.Reject();
            return Task.CompletedTask;
        }

        _telemetry.Push(telemetry);
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

    public Task PushFileCollectionChunk(FileCollectionChunk chunk)
    {
        var pcName = ConnectedPc;
        if (!IsRole(HubRoutes.Roles.Desktop)
            || pcName is null
            || !ContractValidation.IsValidFileCollectionChunk(
                chunk,
                pcName))
        {
            return Task.CompletedTask;
        }

        return _fileCollection.AcceptChunkAsync(
            chunk,
            pcName,
            Context.ConnectionAborted);
    }


    public Task SubmitStudentRegistration(
        StudentRegistrationSubmission submission)
    {
        var pcName = ConnectedPc;
        if (IsRole(HubRoutes.Roles.Desktop)
            && pcName is not null)
        {
            _lessons.Register(submission, pcName);
        }

        return Task.CompletedTask;
    }

    public Task SubmitAssessment(AssessmentSubmission submission)
    {
        var pcName = ConnectedPc;
        if (IsRole(HubRoutes.Roles.Desktop)
            && pcName is not null)
        {
            _lessons.SubmitAssessment(submission, pcName);
        }

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
    public Task ReportDeviceKeyRotation(DeviceKeyRotationReceipt receipt)
    {
        var pcName = ConnectedPc;
        Context.Items.TryGetValue(DeviceItem, out var deviceValue);
        Context.Items.TryGetValue(PendingRotationItem, out var pendingValue);
        var deviceId = deviceValue as string;
        var notice = pendingValue as DeviceKeyRotationNotice;
        var hasIdentity = IsRole(HubRoutes.Roles.Agent)
                          && pcName is not null
                          && deviceId is not null
                          && notice is not null
                          && string.Equals(deviceId, receipt.DeviceId, StringComparison.Ordinal)
                          && receipt.KeyVersion == notice.CurrentKeyVersion;
        if (!hasIdentity)
        {
            _ = TryAuditAuthentication(pcName, "key-rotation-rejected", "identity-or-challenge-missing");
            Context.Abort();
            return Task.CompletedTask;
        }

        var expectedSecret = DeviceCredentialStore.DeriveSecret(
            _sharedSecret,
            _classroomId,
            deviceId!,
            pcName!,
            receipt.KeyVersion);
        if (!DeviceKeyRotationProtocol.ValidateReceipt(
                notice!,
                receipt,
                expectedSecret,
                pcName!))
        {
            _ = TryAuditAuthentication(pcName, "key-rotation-rejected", $"{deviceId}:invalid-proof");
            Context.Abort();
            return Task.CompletedTask;
        }

        if (!TryAuditAuthentication(
                pcName,
                "key-rotation-applied",
                $"{deviceId}:v{receipt.KeyVersion}"))
        {
            Context.Abort();
            return Task.CompletedTask;
        }

        Context.Items[KeyVersionItem] = receipt.KeyVersion;
        Context.Items.Remove(PendingRotationItem);
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

    private async Task SendRotationNoticeIfNeeded()
    {
        if (Context.Items.TryGetValue(LegacyItem, out var legacy)
            && legacy is true) return;
        if (!Context.Items.TryGetValue(DeviceItem, out var deviceValue)
            || deviceValue is not string deviceId
            || !Context.Items.TryGetValue(KeyVersionItem, out var versionValue)
            || versionValue is not int connectedVersion) return;

        var policy = KeyRotationPolicyStore.ReadOrDefault();
        if (connectedVersion >= policy.CurrentVersion) return;

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (Context.Items.TryGetValue(PendingRotationItem, out var pendingValue)
            && pendingValue is DeviceKeyRotationNotice pending
            && pending.CurrentKeyVersion == policy.CurrentVersion
            && now - pending.IssuedAtUnixMs < 30_000)
            return;

        var notice = DeviceKeyRotationProtocol.CreateNotice(
            deviceId,
            policy.CurrentVersion);
        Context.Items[PendingRotationItem] = notice;
        await Clients.Caller.SendAsync(
            HubRoutes.Methods.ReceiveDeviceKeyRotation,
            notice);
        if (!TryAuditAuthentication(
                ConnectedPc,
                "key-rotation-issued",
                $"{deviceId}:v{connectedVersion}->v{policy.CurrentVersion}"))
            Context.Abort();
    }

    private bool TryAuditAuthentication(
        string? pcName,
        string outcome,
        string detail)
    {
        try
        {
            _authorization.RecordSystemEvent(
                "hub.authentication",
                pcName,
                outcome,
                detail);
            return true;
        }
        catch (Exception exception)
        {
            _logger.LogCritical(
                exception,
                "Security audit tidak dapat ditulis; koneksi Hub dihentikan");
            return false;
        }
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