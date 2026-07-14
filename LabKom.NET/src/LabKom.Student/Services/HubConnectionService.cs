using LabKom.Shared.Contracts;
using LabKom.Shared.Hub;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace LabKom.Student.Services;

public class HubConnectionService : IAsyncDisposable
{
    private readonly ILogger<HubConnectionService> _logger;
    private readonly TeacherEndpointStore _endpointStore;
    private readonly MachineIdentity _identity;
    private readonly CaptureProfileState _profileState;
    private readonly PowerService _powerService;
    private readonly FileDownloader _downloader;
    private readonly WebFilterEnforcer _webFilter;
    private readonly AppBlockEnforcer _appBlock;
    private readonly string _sharedSecret;
    private HubConnection? _connection;
    private readonly SemaphoreSlim _connectGate = new(1, 1);

    public event EventHandler<AttentionCommand>? AttentionReceived;
    public event EventHandler<ChatMessage>? ChatReceived;
    public event EventHandler<PowerCommand>? PowerReceived;
    public event EventHandler<CaptureProfileCommand>? CaptureProfileReceived;
    public event EventHandler<FileDistributionNotice>? FileNoticeReceived;
    public event EventHandler<WebFilterPolicy>? WebFilterPolicyReceived;
    public event EventHandler<AppBlockPolicy>? AppBlockPolicyReceived;

    public bool IsConnected => _connection?.State == HubConnectionState.Connected;

    public HubConnectionService(
        ILogger<HubConnectionService> logger,
        TeacherEndpointStore endpointStore,
        MachineIdentity identity,
        CaptureProfileState profileState,
        PowerService powerService,
        FileDownloader downloader,
        WebFilterEnforcer webFilter,
        AppBlockEnforcer appBlock,
        IConfiguration configuration)
    {
        _logger = logger;
        _endpointStore = endpointStore;
        _identity = identity;
        _profileState = profileState;
        _powerService = powerService;
        _downloader = downloader;
        _webFilter = webFilter;
        _appBlock = appBlock;
        _sharedSecret = Environment.GetEnvironmentVariable("LABKOM_SHARED_SECRET")
                        ?? configuration["Agent:SharedSecret"]
                        ?? string.Empty;
    }

    public async Task EnsureConnectedAsync(CancellationToken ct)
    {
        var url = _endpointStore.BuildHubUrl();
        if (_sharedSecret.Length < 16)
        {
            _logger.LogError("LABKOM_SHARED_SECRET/Agent:SharedSecret belum dikonfigurasi.");
            return;
        }
        if (url is null) return;
        if (IsConnected) return;

        await _connectGate.WaitAsync(ct);
        try
        {
            if (IsConnected) return;
            await DisposeConnectionAsync();

            var fullUrl = $"{url}?{HubRoutes.Roles.Key}={HubRoutes.Roles.Agent}&pc={Uri.EscapeDataString(_identity.PcName)}&{HubSecurity.QueryKey}={Uri.EscapeDataString(_sharedSecret)}";

            _connection = new HubConnectionBuilder()
                .WithUrl(fullUrl)
                .WithAutomaticReconnect()
                .Build();

            _connection.On<AttentionCommand>(HubRoutes.Methods.ReceiveAttention,
                cmd => AttentionReceived?.Invoke(this, cmd));
            _connection.On<ChatMessage>(HubRoutes.Methods.ReceiveChat,
                msg => ChatReceived?.Invoke(this, msg));
            _connection.On<PowerCommand>(HubRoutes.Methods.ReceivePowerCommand, cmd =>
            {
                _powerService.Execute(cmd);
                PowerReceived?.Invoke(this, cmd);
            });
            _connection.On<CaptureProfileCommand>(HubRoutes.Methods.ReceiveCaptureProfile, cmd =>
            {
                _profileState.Current = cmd.Profile;
                CaptureProfileReceived?.Invoke(this, cmd);
            });
            _connection.On<FileDistributionNotice>(HubRoutes.Methods.ReceiveFileNotice, async notice =>
            {
                FileNoticeReceived?.Invoke(this, notice);
                await HandleFileNoticeAsync(notice, CancellationToken.None);
            });
            _connection.On<WebFilterPolicy>(HubRoutes.Methods.ReceiveWebFilterPolicy, policy =>
            {
                _webFilter.Apply(policy);
                WebFilterPolicyReceived?.Invoke(this, policy);
            });
            _connection.On<AppBlockPolicy>(HubRoutes.Methods.ReceiveAppBlockPolicy, policy =>
            {
                _appBlock.UpdatePolicy(policy);
                AppBlockPolicyReceived?.Invoke(this, policy);
            });

            await _connection.StartAsync(ct);
            await SendHelloAsync(ct);
            _logger.LogInformation("Terhubung ke Teacher Hub: {Url}", url);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Gagal connect ke {Url}", url);
            await DisposeConnectionAsync();
        }
        finally
        {
            _connectGate.Release();
        }
    }

    public Task SendHelloAsync(CancellationToken ct) =>
        InvokeIfConnectedAsync(HubRoutes.Methods.Hello, BuildPresence(StudentStatus.Online), ct);

    public Task SendHeartbeatAsync(CancellationToken ct) =>
        InvokeIfConnectedAsync(HubRoutes.Methods.Heartbeat, BuildPresence(StudentStatus.Online), ct);

    public Task PushScreenFrameAsync(ScreenFrame frame, CancellationToken ct) =>
        InvokeIfConnectedAsync(HubRoutes.Methods.PushScreenFrame, frame, ct);

    public Task PushActivityAsync(ActivityRecord record, CancellationToken ct) =>
        InvokeIfConnectedAsync(HubRoutes.Methods.PushActivityRecord, record, ct);

    public Task ReportFileProgressAsync(FileDistributionProgress p, CancellationToken ct) =>
        InvokeIfConnectedAsync(HubRoutes.Methods.ReportFileProgress, p, ct);

    public Task SendChatToTeacherAsync(string body, CancellationToken ct)
    {
        var msg = new ChatMessage(
            Guid.NewGuid().ToString("N"),
            ChatDirection.StudentToTeacher,
            _identity.PcName,
            null,
            body,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        return InvokeIfConnectedAsync(HubRoutes.Methods.SendChatToTeacher, msg, ct);
    }

    private async Task HandleFileNoticeAsync(FileDistributionNotice notice, CancellationToken ct)
    {
        await ReportFileProgressAsync(new FileDistributionProgress(
            notice.Id, _identity.PcName, FileDistributionState.Downloading, 0, null,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()), ct);

        var result = await _downloader.DownloadAsync(notice, ct);

        var state = result.Success ? FileDistributionState.Completed : FileDistributionState.Failed;
        await ReportFileProgressAsync(new FileDistributionProgress(
            notice.Id, _identity.PcName, state,
            result.Success ? notice.SizeBytes : 0,
            result.Error,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()), ct);
    }

    private StudentPresence BuildPresence(StudentStatus status) =>
        StudentPresence.Snapshot(_identity.PcName, _identity.MacAddress, _identity.IpAddress, status);

    private async Task InvokeIfConnectedAsync(string method, object payload, CancellationToken ct)
    {
        if (_connection is null || _connection.State != HubConnectionState.Connected) return;
        try { await _connection.InvokeAsync(method, payload, ct); }
        catch (Exception ex) { _logger.LogWarning(ex, "Invoke {Method} gagal", method); }
    }

    private async Task DisposeConnectionAsync()
    {
        if (_connection is not null)
        {
            try { await _connection.DisposeAsync(); } catch { }
            _connection = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await DisposeConnectionAsync();
        _connectGate.Dispose();
    }
}
