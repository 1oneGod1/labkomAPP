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

    public bool IsConnected => _connection?.State == HubConnectionState.Connected;

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
        if (!HubSecurity.IsStrongSecret(_sharedSecret))
        {
            _logger.LogError("LABKOM_SHARED_SECRET/Agent:SharedSecret wajib minimal {Length} karakter.", HubSecurity.MinimumSecretLength);
            return;
        }
        if (endpoint is null || url is null || IsConnected) return;

        await _connectGate.WaitAsync(ct);
        try
        {
            if (IsConnected) return;
            await DisposeConnectionAsync();

            var connectionUrl = HubRoutes.BuildClientUrl(url, HubRoutes.Roles.Agent, _identity.PcName);
            _connection = new HubConnectionBuilder()
                .WithUrl(connectionUrl, options =>
                {
                    options.Headers[HubSecurity.HeaderName] = _sharedSecret;
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

            _connection.On<PowerCommand>(
                HubRoutes.Methods.ReceivePowerCommand,
                HandlePowerCommandAsync);
            _connection.On<WebFilterPolicy>(HubRoutes.Methods.ReceiveWebFilterPolicy,
                policy => _webFilter.Apply(policy));
            _connection.On<AppBlockPolicy>(HubRoutes.Methods.ReceiveAppBlockPolicy,
                policy => _appBlock.UpdatePolicy(policy));

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