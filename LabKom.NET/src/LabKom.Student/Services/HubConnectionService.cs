using LabKom.Shared.Contracts;
using LabKom.Shared.Devices;
using LabKom.Shared.Discovery;
using LabKom.Shared.Hub;
using LabKom.Shared.Security;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace LabKom.Student.Services;

/// <summary>
/// Control-plane connection for the Windows Service. Only privileged operations
/// are accepted here; interactive features belong to LabKom.Student.Desktop.
/// </summary>
public sealed class HubConnectionService : IAsyncDisposable
{
    private readonly ILogger<HubConnectionService> _logger;
    private readonly TeacherEndpointStore _endpointStore;
    private readonly MachineIdentity _identity;
    private readonly PowerService _powerService;
    private readonly WebFilterEnforcer _webFilter;
    private readonly AppBlockEnforcer _appBlock;
    private readonly string _sharedSecret;
    private readonly SemaphoreSlim _connectGate = new(1, 1);
    private HubConnection? _connection;

    public bool IsConnected =>
        _connection?.State == HubConnectionState.Connected;

    private bool HasActiveConnection =>
        _connection?.State is
            HubConnectionState.Connected
            or HubConnectionState.Connecting
            or HubConnectionState.Reconnecting;

    public HubConnectionService(
        ILogger<HubConnectionService> logger,
        TeacherEndpointStore endpointStore,
        MachineIdentity identity,
        PowerService powerService,
        WebFilterEnforcer webFilter,
        AppBlockEnforcer appBlock,
        IConfiguration configuration)
    {
        _logger = logger;
        _endpointStore = endpointStore;
        _identity = identity;
        _powerService = powerService;
        _webFilter = webFilter;
        _appBlock = appBlock;
        _sharedSecret = Environment.GetEnvironmentVariable("LABKOM_SHARED_SECRET")
                        ?? configuration["Agent:SharedSecret"]
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
            _logger.LogError("LABKOM_SHARED_SECRET/Agent:SharedSecret wajib minimal {Length} karakter.", HubSecurity.MinimumSecretLength);
            return;
        }
        if (endpoint is null || url is null || HasActiveConnection) return;

        await _connectGate.WaitAsync(ct);
        try
        {
            if (HasActiveConnection) return;
            await DisposeConnectionAsync();

            var connectionUrl = HubRoutes.BuildClientUrl(url, HubRoutes.Roles.Agent, _identity.PcName);
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

            _connection.Reconnected += async _ =>
            {
                await SendHelloAsync(CancellationToken.None);
                _logger.LogInformation(
                    "Student Agent berhasil reconnect ke Teacher Hub");
            };
            _connection.On<PowerCommand>(
                HubRoutes.Methods.ReceivePowerCommand,
                HandlePowerCommandAsync);
            _connection.On<WebFilterPolicy>(
                HubRoutes.Methods.ReceiveWebFilterPolicy,
                HandleWebFilterPolicyAsync);
            _connection.On<AppBlockPolicy>(
                HubRoutes.Methods.ReceiveAppBlockPolicy,
                HandleAppBlockPolicyAsync);
            _connection.On<DeviceKeyRotationNotice>(
                HubRoutes.Methods.ReceiveDeviceKeyRotation,
                HandleDeviceKeyRotationAsync);

            await _connection.StartAsync(ct);
            await SendHelloAsync(ct);
            _logger.LogInformation("Student Agent terhubung ke Teacher Hub: {Url}", url);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            await DisposeConnectionAsync();
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Student Agent gagal terhubung ke {Url}", url);
            await DisposeConnectionAsync();
        }
        finally
        {
            _connectGate.Release();
        }
    }

    private async Task HandleDeviceKeyRotationAsync(DeviceKeyRotationNotice notice)
    {
        try
        {
            var current = DeviceCredentialStore.Read();
            if (!DeviceKeyRotationProtocol.IsValidNotice(
                    notice,
                    current.DeviceId,
                    current.KeyVersion))
            {
                _logger.LogWarning("Notice rotasi credential perangkat ditolak");
                return;
            }

            var provisioning = ProvisionedSecretStore.Read();
            var rotated = DeviceCredentialStore.RotateFromProvisioning(
                provisioning,
                notice.CurrentKeyVersion);
            var receipt = DeviceKeyRotationProtocol.CreateReceipt(
                notice,
                rotated.Secret,
                rotated.PcName);
            await InvokeIfConnectedAsync(
                HubRoutes.Methods.ReportDeviceKeyRotation,
                receipt,
                CancellationToken.None);
            _logger.LogWarning(
                "Credential perangkat {DeviceId} diputar ke versi {Version}; receipt dikirim ke Teacher",
                rotated.DeviceId,
                rotated.KeyVersion);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Rotasi credential perangkat gagal");
        }
    }

    private async Task HandleWebFilterPolicyAsync(WebFilterPolicy policy)
    {
        var isValid = ContractValidation.IsValidWebFilterPolicy(policy);
        var outcome = isValid
            ? _webFilter.Apply(policy)
            : new CommandExecutionOutcome(
                false,
                "Policy blokir situs tidak valid atau kedaluwarsa");
        await ReportPolicyResultAsync(
            policy.CommandId,
            RemoteCommandKind.WebFilter,
            ResultState(isValid, outcome),
            outcome.Message);
    }

    private async Task HandleAppBlockPolicyAsync(AppBlockPolicy policy)
    {
        var isValid = ContractValidation.IsValidAppBlockPolicy(policy);
        var outcome = isValid
            ? _appBlock.UpdatePolicy(policy)
            : new CommandExecutionOutcome(
                false,
                "Policy blokir aplikasi tidak valid atau kedaluwarsa");
        if (outcome.Success && policy.Enabled)
        {
            var killed = _appBlock.ScanAndKill();
            if (killed > 0)
            {
                outcome = outcome with
                {
                    Message = $"{outcome.Message}; {killed} proses dihentikan",
                };
            }
        }

        await ReportPolicyResultAsync(
            policy.CommandId,
            RemoteCommandKind.AppBlock,
            ResultState(isValid, outcome),
            outcome.Message);
    }

    private static CommandExecutionState ResultState(
        bool isValid,
        CommandExecutionOutcome outcome) =>
        !isValid
            ? CommandExecutionState.Rejected
            : outcome.Success
                ? CommandExecutionState.Applied
                : CommandExecutionState.Failed;

    private Task ReportPolicyResultAsync(
        string commandId,
        RemoteCommandKind kind,
        CommandExecutionState state,
        string message) =>
        InvokeIfConnectedAsync(
            HubRoutes.Methods.ReportCommandResult,
            CommandResult.Create(
                commandId,
                _identity.PcName,
                kind,
                state,
                message),
            CancellationToken.None);
    private async Task HandlePowerCommandAsync(PowerCommand command)
    {
        CommandExecutionState state;
        string message;
        if (!ContractValidation.IsValidPowerCommand(command))
        {
            state = CommandExecutionState.Rejected;
            message = "Perintah power tidak valid atau kedaluwarsa";
        }
        else
        {
            var outcome = _powerService.Execute(command);
            state = outcome.Success
                ? CommandExecutionState.Accepted
                : CommandExecutionState.Failed;
            message = outcome.Message;
        }

        await InvokeIfConnectedAsync(
            HubRoutes.Methods.ReportCommandResult,
            CommandResult.Create(
                command.CommandId,
                _identity.PcName,
                RemoteCommandKind.Power,
                state,
                message),
            CancellationToken.None);
    }

    public Task SendHelloAsync(CancellationToken ct) =>
        InvokeIfConnectedAsync(HubRoutes.Methods.Hello, BuildPresence(StudentStatus.Online), ct);

    public Task SendHeartbeatAsync(CancellationToken ct) =>
        InvokeIfConnectedAsync(HubRoutes.Methods.Heartbeat, BuildPresence(StudentStatus.Online), ct);

    private StudentPresence BuildPresence(StudentStatus status) =>
        StudentPresence.Snapshot(_identity.PcName, _identity.MacAddress, _identity.IpAddress, status);
    public Task SendTelemetryAsync(
        DeviceTelemetry telemetry,
        CancellationToken ct) =>
        InvokeIfConnectedAsync(
            HubRoutes.Methods.PushDeviceTelemetry,
            telemetry,
            ct);

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
            _logger.LogWarning(ex, "Invoke {Method} dari Student Agent gagal", method);
        }
    }

    private async Task DisposeConnectionAsync()
    {
        if (_connection is null) return;
        try
        {
            await _connection.DisposeAsync();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Dispose koneksi Student Agent gagal");
        }
        finally
        {
            _connection = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await DisposeConnectionAsync();
        _connectGate.Dispose();
    }
}