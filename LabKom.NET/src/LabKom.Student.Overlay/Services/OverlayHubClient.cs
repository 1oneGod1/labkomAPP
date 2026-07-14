using LabKom.Shared.Contracts;
using LabKom.Shared.Hub;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace LabKom.Student.Overlay.Services;

public class OverlayHubClient : IAsyncDisposable
{
    private readonly TeacherEndpointStore _endpoints;
    private readonly MachineIdentity _identity;
    private readonly ILogger<OverlayHubClient> _logger;
    private readonly string _sharedSecret;
    private HubConnection? _connection;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public event EventHandler<AttentionCommand>? AttentionReceived;
    public event EventHandler<TeacherBroadcastSignal>? BroadcastSignalReceived;
    public event EventHandler<TeacherFrame>? TeacherFrameReceived;

    public bool IsConnected => _connection?.State == HubConnectionState.Connected;

    public OverlayHubClient(
        TeacherEndpointStore endpoints,
        MachineIdentity identity,
        ILogger<OverlayHubClient> logger,
        IConfiguration configuration)
    {
        _endpoints = endpoints;
        _identity = identity;
        _logger = logger;
        _sharedSecret = Environment.GetEnvironmentVariable("LABKOM_SHARED_SECRET")
                        ?? configuration["Overlay:SharedSecret"]
                        ?? string.Empty;
    }

    public async Task EnsureConnectedAsync(CancellationToken ct)
    {
        var url = _endpoints.BuildHubUrl();
        if (_sharedSecret.Length < 16)
        {
            _logger.LogError("LABKOM_SHARED_SECRET/Overlay:SharedSecret belum dikonfigurasi.");
            return;
        }
        if (url is null || IsConnected) return;

        await _gate.WaitAsync(ct);
        try
        {
            if (IsConnected) return;
            await DisposeConnectionAsync();

            var fullUrl = $"{url}?{HubRoutes.Roles.Key}={HubRoutes.Roles.Overlay}&pc={Uri.EscapeDataString(_identity.PcName)}&{HubSecurity.QueryKey}={Uri.EscapeDataString(_sharedSecret)}";

            _connection = new HubConnectionBuilder()
                .WithUrl(fullUrl)
                .WithAutomaticReconnect()
                .Build();

            _connection.On<AttentionCommand>(HubRoutes.Methods.ReceiveAttention,
                cmd => AttentionReceived?.Invoke(this, cmd));

            _connection.On<TeacherBroadcastSignal>(HubRoutes.Methods.ReceiveTeacherBroadcastSignal,
                sig => BroadcastSignalReceived?.Invoke(this, sig));

            _connection.On<TeacherFrame>(HubRoutes.Methods.ReceiveTeacherFrame,
                frame => TeacherFrameReceived?.Invoke(this, frame));

            await _connection.StartAsync(ct);
            _logger.LogInformation("Overlay terhubung ke Hub: {Url}", url);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Overlay gagal connect");
            await DisposeConnectionAsync();
        }
        finally { _gate.Release(); }
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
        _gate.Dispose();
    }
}
