using System.Windows;
using LabKom.Shared.Contracts;
using LabKom.Student.Overlay.Services;
using LabKom.Student.Overlay.Workers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LabKom.Student.Overlay;

public partial class App : Application
{
    private IHost? _host;
    private OverlayWindow? _overlay;
    private BroadcastWindow? _broadcast;
    private KeyboardHook? _hook;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (!SingleInstance.TryAcquire("Local\\LabKomStudentOverlay"))
        {
            Shutdown();
            return;
        }

        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables();

        builder.Services.AddSingleton<MachineIdentity>();
        builder.Services.AddSingleton<TeacherEndpointStore>();
        builder.Services.AddSingleton<OverlayHubClient>();
        builder.Services.AddHostedService<DiscoveryListener>();
        builder.Services.AddHostedService<OverlayConnectionWorker>();

        _host = builder.Build();

        var hub = _host.Services.GetRequiredService<OverlayHubClient>();
        hub.AttentionReceived += OnAttentionReceived;
        hub.BroadcastSignalReceived += OnBroadcastSignalReceived;
        hub.TeacherFrameReceived += OnTeacherFrameReceived;

        _hook = new KeyboardHook();

        await _host.StartAsync();
    }

    private void OnAttentionReceived(object? sender, AttentionCommand cmd) =>
        Dispatcher.Invoke(() =>
        {
            if (cmd.Enabled)
            {
                _overlay ??= new OverlayWindow();
                _overlay.SetMessage(cmd.Message);
                _hook?.Enable();
                _overlay.Show();
                _overlay.Activate();
                _overlay.Topmost = true;
            }
            else
            {
                _hook?.Disable();
                if (_overlay is not null)
                {
                    _overlay.Hide();
                    _overlay.Close();
                    _overlay = null;
                }
            }
        });

    private void OnBroadcastSignalReceived(object? sender, TeacherBroadcastSignal sig) =>
        Dispatcher.Invoke(() =>
        {
            if (sig.Active)
            {
                _broadcast ??= new BroadcastWindow();
                _hook?.Enable();
                _broadcast.Show();
                _broadcast.Activate();
                _broadcast.Topmost = true;
            }
            else
            {
                // Jangan disable hook jika overlay attention masih aktif.
                if (_overlay is null) _hook?.Disable();
                if (_broadcast is not null)
                {
                    _broadcast.Hide();
                    _broadcast.Close();
                    _broadcast = null;
                }
            }
        });

    private void OnTeacherFrameReceived(object? sender, TeacherFrame frame) =>
        Dispatcher.Invoke(() =>
        {
            if (_broadcast is null)
            {
                // Frame masuk sebelum sinyal start — buat window otomatis.
                _broadcast = new BroadcastWindow();
                _hook?.Enable();
                _broadcast.Show();
                _broadcast.Topmost = true;
            }
            _broadcast.UpdateFrame(frame.JpegData);
        });

    protected override async void OnExit(ExitEventArgs e)
    {
        _hook?.Dispose();
        if (_host is not null)
        {
            await _host.StopAsync(TimeSpan.FromSeconds(2));
            _host.Dispose();
        }
        base.OnExit(e);
    }
}
