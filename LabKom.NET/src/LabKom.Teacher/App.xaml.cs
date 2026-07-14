using System.IO;
using System.Windows;
using LabKom.Data;
using LabKom.Teacher.Services;
using LabKom.Teacher.ViewModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LabKom.Teacher;

public partial class App : Application
{
    private IHost? _host;

    public IServiceProvider Services => _host?.Services
        ?? throw new InvalidOperationException("Host belum siap.");

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .AddEnvironmentVariables();

        builder.Logging.AddDebug();

        // EF Core SQLite
        var connStr = ResolveDbConnectionString(builder.Configuration);
        builder.Services.AddDbContext<LabKomDbContext>(opt => opt.UseSqlite(connStr));

        // Singletons
        builder.Services.AddSingleton<PresenceRegistry>();
        builder.Services.AddSingleton<HubContextHolder>();
        builder.Services.AddSingleton<ActivityFeed>();
        builder.Services.AddSingleton<WakeOnLanService>();
        builder.Services.AddSingleton<RemoteCommandService>();
        builder.Services.AddSingleton<FileDistributionService>();
        builder.Services.AddSingleton<TeacherScreenBroadcaster>();

        // Hosted
        builder.Services.AddHostedService<HubHostService>();
        builder.Services.AddHostedService<DiscoveryBroadcaster>();
        builder.Services.AddHostedService<PersistenceService>();

        // UI
        builder.Services.AddSingleton<MainViewModel>();
        builder.Services.AddSingleton<MainWindow>();

        _host = builder.Build();
        await _host.StartAsync();

        var window = _host.Services.GetRequiredService<MainWindow>();
        window.DataContext = _host.Services.GetRequiredService<MainViewModel>();
        window.Show();
    }

    private static string ResolveDbConnectionString(IConfiguration config)
    {
        var raw = config.GetValue<string>("Database:ConnectionString")
                  ?? "Data Source=labkom.db";
        // Expand %LOCALAPPDATA% & sejenisnya.
        raw = Environment.ExpandEnvironmentVariables(raw);
        // Pastikan folder ada jika file path absolute.
        var match = System.Text.RegularExpressions.Regex.Match(raw, @"Data Source=([^;]+)");
        if (match.Success)
        {
            var path = match.Groups[1].Value.Trim();
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        }
        return raw;
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            await _host.StopAsync(TimeSpan.FromSeconds(2));
            _host.Dispose();
        }
        base.OnExit(e);
    }
}
