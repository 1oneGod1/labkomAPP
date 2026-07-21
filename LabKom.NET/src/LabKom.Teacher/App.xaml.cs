using System.IO;
using System.Windows;
using LabKom.Data;
using LabKom.Shared.Hub;
using LabKom.Shared.Security;
using LabKom.Shared.Updates;
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
        DispatcherUnhandledException += (_, args) =>
        {
            var failure = args.Exception.GetBaseException();
            if (failure is not UnauthorizedAccessException
                and not InvalidDataException)
                return;
            MessageBox.Show(
                failure.Message,
                "Operasi keamanan ditolak",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            args.Handled = true;
        };


        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile("appsettings.Local.json", optional: true)
            .AddEnvironmentVariables();

        var sharedSecret = ProvisionedSecretStore.Resolve(builder.Configuration["Teacher:SharedSecret"]);
        if (!HubSecurity.IsStrongSecret(sharedSecret))
        {
            MessageBox.Show(
                $"LABKOM_SHARED_SECRET wajib diisi minimal {HubSecurity.MinimumSecretLength} karakter.",
                "Konfigurasi LabKom belum lengkap",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(-1);
            return;
        }
        builder.Configuration["Teacher:SharedSecret"] = sharedSecret;
        if (ProvisionedSecretStore.TryRead(null, out var teacherProvisioning))
        {
            builder.Configuration["Security:ClassroomId"] =
                teacherProvisioning.ClassroomId;
        }

        builder.Logging.AddDebug();
        var auditPath = Environment.ExpandEnvironmentVariables(
            builder.Configuration["Security:AuditPath"]
            ?? "%LOCALAPPDATA%\\LabKom\\Audit\\security-audit.jsonl");
        var securityAudit = new SecurityAuditJournal(
            sharedSecret!,
            auditPath);
        if (!securityAudit.IntegrityValid)
        {
            MessageBox.Show(
                "Integritas security audit tidak valid. Kontrol kelas dihentikan sampai log diperiksa.",
                "Security audit rusak",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(-2);
            return;
        }
        builder.Services.AddSingleton(securityAudit);
        builder.Services.AddSingleton<TeacherAuthorizationService>();


        // EF Core SQLite
        var connStr = ResolveDbConnectionString(builder.Configuration);
        builder.Services.AddDbContext<LabKomDbContext>(opt => opt.UseSqlite(connStr));

        // Singletons
        builder.Services.AddSingleton<TeacherCertificateProvider>();
        builder.Services.AddSingleton<PresenceRegistry>();
        builder.Services.AddSingleton<AttentionStateStore>();
        builder.Services.AddSingleton<ClassPolicyStateStore>();
        builder.Services.AddSingleton<ClassroomSessionIdentity>();
        builder.Services.AddSingleton<ClassroomGroupStore>();
        builder.Services.AddSingleton<ClassroomLessonStore>();
        builder.Services.AddSingleton<ClassroomLessonService>();
        builder.Services.AddSingleton<HubContextHolder>();
        builder.Services.AddSingleton<ActivityFeed>();
        builder.Services.AddSingleton<WakeOnLanService>();
        builder.Services.AddSingleton<RemoteCommandService>();
        builder.Services.AddSingleton<RemoteControlService>();
        builder.Services.AddSingleton<TechnicianConsoleService>();
        builder.Services.AddSingleton<TelemetryRegistry>();
        builder.Services.AddSingleton<TelemetrySessionRecorder>();
        builder.Services.AddSingleton<FileDistributionService>();
        builder.Services.AddSingleton<FileCollectionService>();
        builder.Services.AddSingleton<TeacherScreenBroadcaster>();

        // Hosted
        builder.Services.AddHostedService<HubHostService>();
        builder.Services.AddHostedService<DiscoveryBroadcaster>();
        builder.Services.AddHostedService<PersistenceService>();
        builder.Services.AddSingleton<IHostedService>(serviceProvider =>
            serviceProvider.GetRequiredService<TelemetrySessionRecorder>());

        // UI
        builder.Services.AddSingleton<MainViewModel>();
        builder.Services.AddSingleton<MainWindow>();

        _host = builder.Build();
        await _host.StartAsync();
        UpdateHealthReporter.MarkHealthy(
            "Teacher",
            typeof(App).Assembly.GetName().Version?.ToString(3) ?? "0.0.0");

        if (e.Args.Contains("--health-check", StringComparer.OrdinalIgnoreCase)
            || Environment.GetCommandLineArgs().Contains(
                "--health-check", StringComparer.OrdinalIgnoreCase))
        {
            Environment.Exit(0);
        }

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
