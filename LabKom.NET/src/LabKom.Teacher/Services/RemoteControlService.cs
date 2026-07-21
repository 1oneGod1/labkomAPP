using System.Collections.Concurrent;
using LabKom.Shared.Contracts;
using LabKom.Shared.Hub;
using LabKom.Shared.Security;
using LabKom.Teacher.Hub;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace LabKom.Teacher.Services;

/// <summary>
/// Sesi remote Teacher yang terikat target, mode, expiry, dan SessionId.
/// Lifecycle diaudit; input hanya diteruskan untuk sesi control aktif yang dikelola service.
/// </summary>
public sealed class RemoteControlService
{
    private readonly ConcurrentDictionary<string, RemoteSessionCommand> _byPc =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HubContextHolder _holder;
    private readonly PresenceRegistry _registry;
    private readonly TeacherAuthorizationService _authorization;
    private readonly ILogger<RemoteControlService> _logger;

    public RemoteControlService(
        HubContextHolder holder,
        PresenceRegistry registry,
        TeacherAuthorizationService authorization,
        ILogger<RemoteControlService> logger)
    {
        _holder = holder;
        _registry = registry;
        _authorization = authorization;
        _logger = logger;
        _registry.RemoteSessionStatusReceived += OnRemoteSessionStatusReceived;
    }

    public event EventHandler<RemoteSessionStatus>? StatusReceived;

    private IHubContext<TeacherHub> Hub =>
        _holder.HubContext
        ?? throw new InvalidOperationException("Hub belum siap.");

    public async Task<RemoteSessionCommand> StartAsync(
        string pcName,
        RemoteSessionMode mode,
        string? monitorId,
        int ttlSeconds = 120)
    {
        var permission = mode == RemoteSessionMode.Control
            ? TeacherPermission.RemoteControl
            : TeacherPermission.ViewClassroom;
        _authorization.Demand(
            permission,
            mode == RemoteSessionMode.Control
                ? "remote.control.start"
                : "remote.view.start",
            pcName);

        if (_byPc.TryRemove(pcName, out var previous))
        {
            await SendSessionAsync(
                previous.Stop("Diganti sesi remote baru"));
        }

        var session = RemoteSessionCommand.Start(
            pcName,
            mode,
            monitorId,
            ttlSeconds);
        if (!ContractValidation.IsValidRemoteSessionCommand(
                session,
                pcName))
        {
            throw new ArgumentException(
                "Target atau monitor sesi remote tidak valid.",
                nameof(pcName));
        }

        _byPc[pcName] = session;
        try
        {
            await SendSessionAsync(session);
            return session;
        }
        catch
        {
            _byPc.TryRemove(
                new KeyValuePair<string, RemoteSessionCommand>(
                    pcName,
                    session));
            throw;
        }
    }

    public async Task<RemoteSessionCommand?> RenewAsync(
        RemoteSessionCommand session,
        int ttlSeconds = 120)
    {
        if (!_byPc.TryGetValue(session.TargetPcName, out var current)
            || !SameSession(current, session))
        {
            return null;
        }

        var renewed = current.Renew(ttlSeconds);
        _byPc[current.TargetPcName] = renewed;
        await SendSessionAsync(renewed);
        return renewed;
    }

    public async Task SendInputAsync(
        RemoteSessionCommand session,
        RemoteInputCommand input)
    {
        if (!_byPc.TryGetValue(session.TargetPcName, out var current)
            || !SameSession(current, session)
            || current.Mode != RemoteSessionMode.Control
            || current.ExpiresAtUnixMs
                < DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            || !ContractValidation.IsValidRemoteInput(
                input,
                current.TargetPcName)
            || !string.Equals(
                current.SessionId,
                input.SessionId,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Sesi remote control tidak aktif atau input ditolak.");
        }

        await Hub.Clients
            .Group(HubRoutes.Groups.ForPcRole(
                current.TargetPcName,
                HubRoutes.Roles.Desktop))
            .SendAsync(HubRoutes.Methods.ReceiveRemoteInput, input);
    }

    public async Task StopAsync(
        RemoteSessionCommand session,
        string reason)
    {
        if (!_byPc.TryGetValue(session.TargetPcName, out var current)
            || !SameSession(current, session))
        {
            return;
        }

        _authorization.Demand(
            current.Mode == RemoteSessionMode.Control
                ? TeacherPermission.RemoteControl
                : TeacherPermission.ViewClassroom,
            current.Mode == RemoteSessionMode.Control
                ? "remote.control.stop"
                : "remote.view.stop",
            current.TargetPcName);
        _byPc.TryRemove(
            new KeyValuePair<string, RemoteSessionCommand>(
                current.TargetPcName,
                current));
        await SendSessionAsync(current.Stop(reason));
    }

    private Task SendSessionAsync(RemoteSessionCommand session) =>
        Hub.Clients
            .Group(HubRoutes.Groups.ForPcRole(
                session.TargetPcName,
                HubRoutes.Roles.Desktop))
            .SendAsync(
                HubRoutes.Methods.ReceiveRemoteSession,
                session);

    private void OnRemoteSessionStatusReceived(
        object? sender,
        RemoteSessionStatus status)
    {
        if (status.State != RemoteSessionState.Active
            && _byPc.TryGetValue(status.PcName, out var current)
            && string.Equals(
                current.SessionId,
                status.SessionId,
                StringComparison.Ordinal))
        {
            _byPc.TryRemove(
                new KeyValuePair<string, RemoteSessionCommand>(
                    status.PcName,
                    current));
        }

        if (status.State is
            RemoteSessionState.EmergencyReleased
            or RemoteSessionState.Expired
            or RemoteSessionState.Rejected)
        {
            try
            {
                _authorization.RecordSystemEvent(
                    "remote.session.status",
                    status.PcName,
                    status.State.ToString(),
                    $"{status.SessionId};mode={status.Mode};inputs={status.AcceptedInputCount};{status.Message}");
            }
            catch (Exception exception)
            {
                _logger.LogCritical(
                    exception,
                    "Status keamanan sesi remote gagal diaudit");
            }
        }

        StatusReceived?.Invoke(this, status);
    }

    private static bool SameSession(
        RemoteSessionCommand left,
        RemoteSessionCommand right) =>
        string.Equals(
            left.SessionId,
            right.SessionId,
            StringComparison.Ordinal);
}
