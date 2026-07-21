using System.Diagnostics;
using System.Net.Http;
using LabKom.Shared.Contracts;
using LabKom.Shared.Devices;
using LabKom.Shared.Discovery;
using LabKom.Shared.Hub;
using LabKom.Shared.Security;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace LabKom.Student.Desktop.Services;

/// <summary>
/// Connection for the student's interactive session. It owns UI commands,
/// screen/activity capture, classroom chat, and user-facing file delivery.
/// </summary>
public sealed class DesktopHubClient : IAsyncDisposable
{
    private readonly TeacherEndpointStore _endpointStore;
    private readonly MachineIdentity _identity;
    private readonly CaptureProfileState _profileState;
    private readonly FileDownloader _downloader;
    private readonly FileCollectionClient _fileCollection;
    private readonly ILogger<DesktopHubClient> _logger;
    private readonly RemoteSessionController _remoteSessions;
    private readonly RemoteInputExecutor _remoteInput;
    private readonly string _sharedSecret;
    private readonly SemaphoreSlim _connectGate = new(1, 1);
    private const int MaximumDownloadAttempts = 3;

    private readonly SemaphoreSlim _fileDownloadGate = new(1, 1);
    private readonly SemaphoreSlim _fileCollectionGate = new(1, 1);
    private readonly CancellationTokenSource _disposeCts = new();
    private HubConnection? _connection;

    public event EventHandler<ClassroomStateSnapshot>? ClassroomStateReceived;
    public event EventHandler<AttentionCommand>? AttentionReceived;
    public event EventHandler<ChatMessage>? ChatReceived;
    public event EventHandler<TeacherBroadcastSignal>? BroadcastSignalReceived;
    public event EventHandler<TeacherFrame>? TeacherFrameReceived;
    public event EventHandler<LessonSnapshot>? LessonReceived;

    public bool IsConnected =>
        _connection?.State == HubConnectionState.Connected;

    private bool HasActiveConnection =>
        _connection?.State is
            HubConnectionState.Connected
            or HubConnectionState.Connecting
            or HubConnectionState.Reconnecting;

    public DesktopHubClient(
        TeacherEndpointStore endpoints,
        MachineIdentity identity,
        RemoteSessionController remoteSessions,
        RemoteInputExecutor remoteInput,
        CaptureProfileState profileState,
        FileDownloader downloader,
        FileCollectionClient fileCollection,
        ILogger<DesktopHubClient> logger,
        IConfiguration configuration)
    {
        _remoteSessions = remoteSessions;
        _remoteInput = remoteInput;
        _remoteSessions.StatusChanged += OnRemoteSessionStatusChanged;
        _endpointStore = endpoints;
        _identity = identity;
        _profileState = profileState;
        _downloader = downloader;
        _fileCollection = fileCollection;
        _logger = logger;
        _sharedSecret = Environment.GetEnvironmentVariable("LABKOM_SHARED_SECRET")
                        ?? configuration["Desktop:SharedSecret"]
                        ?? string.Empty;
    }

    public async Task EnsureConnectedAsync(CancellationToken ct)
    {
        var snapshot = _endpointStore.GetFreshSnapshot();
        var endpoint = snapshot?.Beacon;
        var url = snapshot?.HubUrl;
        var authentication = DeviceCredentialStore.Resolve(_sharedSecret);
        if (!HubSecurity.IsStrongSecret(authentication.Secret))
        {
            _logger.LogError("LABKOM_SHARED_SECRET/Desktop:SharedSecret wajib minimal {Length} karakter.", HubSecurity.MinimumSecretLength);
            return;
        }
        if (endpoint is null || url is null || HasActiveConnection) return;

        await _connectGate.WaitAsync(ct);
        try
        {
            if (HasActiveConnection) return;
            await DisposeConnectionAsync();

            var connectionUrl = HubRoutes.BuildClientUrl(url, HubRoutes.Roles.Desktop, _identity.PcName);
            _connection = new HubConnectionBuilder()
                .WithUrl(connectionUrl, options =>
                {
                    options.Headers[HubSecurity.HeaderName] = authentication.Secret;
                    options.Headers[HubSecurity.PcNameHeaderName] = _identity.PcName;
                    if (!authentication.IsLegacy)
                    {
                        options.Headers[HubSecurity.DeviceIdHeaderName] =
                            authentication.DeviceId!;
                        options.Headers[HubSecurity.KeyVersionHeaderName] =
                            authentication.KeyVersion!.Value.ToString(
                                System.Globalization.CultureInfo.InvariantCulture);
                    }
                    options.HttpMessageHandlerFactory = handler =>
                    {
                        if (handler is HttpClientHandler httpHandler)
                        {
                            httpHandler.ServerCertificateCustomValidationCallback =
                                (_, certificate, _, _) => CertificatePin.Matches(certificate, endpoint.CertificateSha256);
                        }
                        return handler;
                    };
                    options.WebSocketConfiguration = webSocket =>
                        webSocket.RemoteCertificateValidationCallback =
                            (_, certificate, _, _) => CertificatePin.Matches(certificate, endpoint.CertificateSha256);
                })
                .WithAutomaticReconnect(new[]
                {
                    TimeSpan.Zero,
                    TimeSpan.FromSeconds(2),
                    TimeSpan.FromSeconds(5),
                    TimeSpan.FromSeconds(10),
                })
                .Build();

            _connection.On<ClassroomStateSnapshot>(
                HubRoutes.Methods.ReceiveClassroomStateSnapshot,
                snapshot =>
                {
                    if (ContractValidation.IsValidClassroomStateSnapshot(
                            snapshot,
                            _identity.PcName))
                    {
                        ClassroomStateReceived?.Invoke(this, snapshot);
                    }
                });
            _connection.On<LessonSnapshot>(
                HubRoutes.Methods.ReceiveLessonSnapshot,
                lesson =>
                {
                    if (ContractValidation.IsValidLessonSnapshot(
                            lesson,
                            _identity.PcName))
                    {
                        LessonReceived?.Invoke(this, lesson);
                    }
                });
            _connection.On<AttentionCommand>(HubRoutes.Methods.ReceiveAttention, command =>
            {
                if (ContractValidation.IsValidAttentionCommand(command, _identity.PcName))
                {
                    AttentionReceived?.Invoke(this, command);
                }
            });
            _connection.On<ChatMessage>(HubRoutes.Methods.ReceiveChat, message =>
            {
                if (message.Direction is ChatDirection.TeacherToStudent or ChatDirection.Broadcast
                    && ContractValidation.IsValidChat(message))
                {
                    ChatReceived?.Invoke(this, message);
                }
            });
            _connection.On<TeacherBroadcastSignal>(
                HubRoutes.Methods.ReceiveTeacherBroadcastSignal,
                signal =>
                {
                    if (ContractValidation.IsValidTeacherBroadcastSignal(signal))
                    {
                        BroadcastSignalReceived?.Invoke(this, signal);
                    }
                });
            _connection.On<TeacherFrame>(HubRoutes.Methods.ReceiveTeacherFrame, frame =>
            {
                if (ContractValidation.IsValidTeacherFrame(frame))
                {
                    TeacherFrameReceived?.Invoke(this, frame);
                }
            });
            _connection.On<CaptureProfileCommand>(HubRoutes.Methods.ReceiveCaptureProfile,
                command => _profileState.TryApply(command));
            _connection.On<FileDistributionNotice>(HubRoutes.Methods.ReceiveFileNotice,
                HandleFileNoticeSerializedAsync);
            _connection.On<FileCollectionRequest>(
                HubRoutes.Methods.ReceiveFileCollectionRequest,
                HandleFileCollectionSerializedAsync);
            _connection.On<RemoteSessionCommand>(
                HubRoutes.Methods.ReceiveRemoteSession,
                command => _remoteSessions.TryApply(command));
            _connection.On<RemoteInputCommand>(
                HubRoutes.Methods.ReceiveRemoteInput,
                input =>
                {
                    if (_remoteSessions.TryAccept(input, out var accepted))
                    {
                        _remoteInput.TryExecute(accepted);
                    }
                });
            _connection.Reconnecting += _ =>
            {
                _remoteSessions.EndLocal(
                    RemoteSessionState.Ended,
                    "Koneksi Teacher terputus");
                return Task.CompletedTask;
            };
            _connection.Closed += _ =>
            {
                _remoteSessions.EndLocal(
                    RemoteSessionState.Ended,
                    "Koneksi Teacher ditutup");
                return Task.CompletedTask;
            };

            await _connection.StartAsync(ct);
            _logger.LogInformation("Desktop siswa terhubung ke Teacher Hub: {Url}", url);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            await DisposeConnectionAsync();
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Desktop siswa gagal terhubung ke {Url}", url);
            await DisposeConnectionAsync();
        }
        finally
        {
            _connectGate.Release();
        }
    }

    public async Task ResetConnectionAsync(CancellationToken ct = default)
    {
        await _connectGate.WaitAsync(ct);
        try
        {
            await DisposeConnectionAsync();
        }
        finally
        {
            _connectGate.Release();
        }
    }

    public Task PushScreenFrameAsync(ScreenFrame frame, CancellationToken ct) =>
        InvokeIfConnectedAsync(HubRoutes.Methods.PushScreenFrame, frame, ct);

    public async Task<int?> PushScreenFrameMeasuredAsync(
        ScreenFrame frame,
        CancellationToken ct)
    {
        var connection = _connection;
        if (connection?.State != HubConnectionState.Connected) return null;

        var stopwatch = Stopwatch.StartNew();
        try
        {
            await connection.InvokeAsync(
                HubRoutes.Methods.PushScreenFrame,
                frame,
                ct);
            return (int)Math.Clamp(
                stopwatch.Elapsed.TotalMilliseconds,
                0,
                60_000);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Pengiriman frame layar gagal");
            return null;
        }
    }

    public Task PushMonitorInventoryAsync(MonitorInventory inventory, CancellationToken ct) =>
        InvokeIfConnectedAsync(HubRoutes.Methods.PushMonitorInventory, inventory, ct);

    public Task PushActivityAsync(ActivityRecord record, CancellationToken ct) =>
        InvokeIfConnectedAsync(HubRoutes.Methods.PushActivityRecord, record, ct);

    public Task ReportCommandResultAsync(
        string commandId,
        RemoteCommandKind kind,
        CommandExecutionState state,
        string? message = null,
        CancellationToken ct = default) =>
        InvokeIfConnectedAsync(
            HubRoutes.Methods.ReportCommandResult,
            CommandResult.Create(commandId, _identity.PcName, kind, state, message),
            ct);

    public Task SendChatToTeacherAsync(string body, CancellationToken ct = default)
    {
        var message = new ChatMessage(
            Guid.NewGuid().ToString("N"),
            ChatDirection.StudentToTeacher,
            _identity.PcName,
            null,
            body?.Trim() ?? string.Empty,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        if (!ContractValidation.IsValidChat(
                message,
                _identity.PcName,
                ChatDirection.StudentToTeacher))
        {
            throw new ArgumentException("Pesan chat tidak valid.", nameof(body));
        }

        return InvokeIfConnectedAsync(HubRoutes.Methods.SendChatToTeacher, message, ct);
    }

    public Task SubmitRegistrationAsync(
        StudentRegistrationSubmission submission,
        CancellationToken cancellationToken = default)
    {
        if (!ContractValidation.IsValidStudentRegistration(
                submission,
                _identity.PcName))
        {
            throw new ArgumentException(
                "Data register siswa tidak valid.",
                nameof(submission));
        }

        return InvokeIfConnectedAsync(
            HubRoutes.Methods.SubmitStudentRegistration,
            submission,
            cancellationToken);
    }

    public Task SubmitAssessmentAsync(
        AssessmentSubmission submission,
        CancellationToken cancellationToken = default)
    {
        if (!ContractValidation.IsValidAssessmentSubmission(
                submission,
                _identity.PcName))
        {
            throw new ArgumentException(
                "Jawaban assessment tidak valid.",
                nameof(submission));
        }

        return InvokeIfConnectedAsync(
            HubRoutes.Methods.SubmitAssessment,
            submission,
            cancellationToken);
    }


    public Task ReportFileProgressAsync(FileDistributionProgress progress, CancellationToken ct) =>
        InvokeIfConnectedAsync(HubRoutes.Methods.ReportFileProgress, progress, ct);

    private async Task HandleFileCollectionSerializedAsync(
        FileCollectionRequest request)
    {
        var cancellationToken = _disposeCts.Token;
        await _fileCollectionGate.WaitAsync(cancellationToken);
        try
        {
            await _fileCollection.CollectAsync(
                request,
                (chunk, token) => InvokeIfConnectedAsync(
                    HubRoutes.Methods.PushFileCollectionChunk,
                    chunk,
                    token),
                cancellationToken);
        }
        catch (OperationCanceledException) when (
            cancellationToken.IsCancellationRequested)
        {
            // Desktop sedang berhenti.
        }
        finally
        {
            _fileCollectionGate.Release();
        }
    }


    private async Task HandleFileNoticeSerializedAsync(FileDistributionNotice notice)
    {
        var ct = _disposeCts.Token;
        try
        {
            await _fileDownloadGate.WaitAsync(ct);
            try
            {
                DownloadResult? result = null;
                for (var attempt = 1;
                     attempt <= MaximumDownloadAttempts;
                     attempt++)
                {
                    await ReportFileProgressAsync(
                        Progress(
                            notice,
                            FileDistributionState.Downloading,
                            0,
                            null),
                        ct);
                    result = await _downloader.DownloadAsync(
                        notice,
                        (bytes, progressCt) => ReportFileProgressAsync(
                            Progress(
                                notice,
                                FileDistributionState.Downloading,
                                bytes,
                                null),
                            progressCt),
                        ct);
                    if (result.Success) break;

                    if (attempt < MaximumDownloadAttempts)
                    {
                        _logger.LogWarning(
                            "Download {NoticeId} gagal pada percobaan {Attempt}; retry",
                            notice.Id,
                            attempt);
                        await Task.Delay(
                            TimeSpan.FromSeconds(attempt * 2),
                            ct);
                    }
                }

                var finalResult = result
                    ?? throw new InvalidOperationException(
                        "Download tidak pernah dijalankan.");
                await ReportFileProgressAsync(
                    Progress(
                        notice,
                        finalResult.Success
                            ? FileDistributionState.Completed
                            : FileDistributionState.Failed,
                        finalResult.BytesReceived,
                        finalResult.Error),
                    ct);
            }
            finally
            {
                _fileDownloadGate.Release();
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Desktop process sedang berhenti.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Distribusi file {NoticeId} gagal", notice.Id);
        }
    }
    private void OnRemoteSessionStatusChanged(
        object? sender,
        RemoteSessionStatus status)
    {
        if (_disposeCts.IsCancellationRequested) return;
        _ = InvokeIfConnectedAsync(
            HubRoutes.Methods.ReportRemoteSessionStatus,
            status,
            _disposeCts.Token);
    }


    private FileDistributionProgress Progress(
        FileDistributionNotice notice,
        FileDistributionState state,
        long bytesReceived,
        string? error) => new(
            notice.Id,
            _identity.PcName,
            state,
            bytesReceived,
            error,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

    private async Task InvokeIfConnectedAsync(string method, object payload, CancellationToken ct)
    {
        var connection = _connection;
        if (connection?.State != HubConnectionState.Connected) return;

        try
        {
            await connection.InvokeAsync(method, payload, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Invoke {Method} dari desktop siswa gagal", method);
        }
    }

    private async Task DisposeConnectionAsync()
    {
        _remoteSessions.EndLocal(
            RemoteSessionState.Ended,
            "Koneksi Desktop di-reset");
        if (_connection is null) return;
        try
        {
            await _connection.DisposeAsync();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Dispose koneksi desktop siswa gagal");
        }
        finally
        {
            _connection = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        _remoteSessions.StatusChanged -= OnRemoteSessionStatusChanged;
        _disposeCts.Cancel();
        await DisposeConnectionAsync();
        _disposeCts.Dispose();
        _connectGate.Dispose();
        _fileDownloadGate.Dispose();
        _fileCollectionGate.Dispose();
    }
}