using LabKom.Shared.Hub;
using LabKom.Shared.Security;
using LabKom.Teacher.Hub;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LabKom.Teacher.Services;

/// <summary>
/// Membungkus ASP.NET Core minimal host (SignalR + Kestrel) sebagai BackgroundService.
/// Setelah start, IHubContext di-publish ke HubContextHolder agar bisa
/// dikonsumsi RemoteCommandService dari DI utama (WPF).
/// </summary>
public class HubHostService : IHostedService
{
    private readonly IConfiguration _config;
    private readonly PresenceRegistry _registry;
    private readonly TeacherCertificateProvider _certificate;
    private readonly HubContextHolder _holder;
    private readonly FileDistributionService _files;
    private readonly FileCollectionService _fileCollection;
    private readonly ActivityFeed _feed;
    private readonly ILoggerFactory _loggerFactory;
    private readonly AttentionStateStore _attentionState;
    private readonly ClassPolicyStateStore _policyState;
    private readonly ClassroomSessionIdentity _sessionIdentity;
    private readonly ClassroomLessonService _lessons;
    private readonly TeacherScreenBroadcaster _screenBroadcaster;
    private readonly TeacherAuthorizationService _authorization;
    private readonly TelemetryRegistry _telemetry;
    private WebApplication? _app;

    public HubHostService(
        IConfiguration config,
        PresenceRegistry registry,
        TeacherCertificateProvider certificate,
        HubContextHolder holder,
        FileDistributionService files,
        FileCollectionService fileCollection,
        ActivityFeed feed,
        AttentionStateStore attentionState,
        ClassPolicyStateStore policyState,
        ClassroomSessionIdentity sessionIdentity,
        ClassroomLessonService lessons,
        TeacherScreenBroadcaster screenBroadcaster,
        TeacherAuthorizationService authorization,
        TelemetryRegistry telemetry,
        ILoggerFactory loggerFactory)
    {
        _config = config;
        _registry = registry;
        _certificate = certificate;
        _holder = holder;
        _files = files;
        _fileCollection = fileCollection;
        _feed = feed;
        _loggerFactory = loggerFactory;
        _attentionState = attentionState;
        _policyState = policyState;
        _sessionIdentity = sessionIdentity;
        _lessons = lessons;
        _screenBroadcaster = screenBroadcaster;
        _authorization = authorization;
        _telemetry = telemetry;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var port = _config.GetValue("Teacher:HubPort", 41235);
        var sharedSecret = Environment.GetEnvironmentVariable("LABKOM_SHARED_SECRET")
                           ?? _config["Teacher:SharedSecret"]
                           ?? string.Empty;
        if (!HubSecurity.IsStrongSecret(sharedSecret))
        {
            throw new InvalidOperationException("LABKOM_SHARED_SECRET/Teacher:SharedSecret wajib diisi minimal 32 karakter.");
        }
        var classroomId = _config["Security:ClassroomId"] ?? string.Empty;
        var allowLegacy = _config.GetValue(
            "Security:AllowLegacySharedSecret",
            true);

        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(_loggerFactory);
        builder.Services.AddSingleton(_config);
        builder.Services.AddSingleton(_registry);
        builder.Services.AddSingleton(_feed);
        builder.Services.AddSingleton(_fileCollection);
        builder.Services.AddSingleton(_attentionState);
        builder.Services.AddSingleton(_policyState);
        builder.Services.AddSingleton(_sessionIdentity);
        builder.Services.AddSingleton(_lessons);
        builder.Services.AddSingleton(_screenBroadcaster);
        builder.Services.AddSingleton(_authorization);
        builder.Services.AddSingleton(_telemetry);
        builder.Services.AddSignalR(options =>
        {
            options.EnableDetailedErrors = false;
            options.MaximumReceiveMessageSize = 4 * 1024 * 1024;
        });

        builder.WebHost.ConfigureKestrel(opt =>
        {
            opt.ListenAnyIP(port, listen => listen.UseHttps(_certificate.Certificate));
            // File besar bisa di-upload via HTTP (file distribution dari guru → siswa).
            opt.Limits.MaxRequestBodySize = 200 * 1024 * 1024; // 200MB
        });

        _app = builder.Build();
        _app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/files"))
            {
                var supplied = context.Request.Headers[HubSecurity.HeaderName].ToString();
                var deviceId = context.Request.Headers[HubSecurity.DeviceIdHeaderName].ToString();
                var pcName = context.Request.Headers[HubSecurity.PcNameHeaderName].ToString();
                var rawVersion = context.Request.Headers[HubSecurity.KeyVersionHeaderName].ToString();
                int? keyVersion = int.TryParse(
                    rawVersion,
                    System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var parsedVersion)
                        ? parsedVersion
                        : null;
                var policy = KeyRotationPolicyStore.ReadOrDefault();
                var authentication = Guid.TryParseExact(classroomId, "N", out _)
                    ? DeviceAuthentication.Validate(
                        sharedSecret,
                        classroomId,
                        supplied,
                        deviceId,
                        pcName,
                        keyVersion,
                        policy.CurrentVersion,
                        policy.AcceptedPreviousVersions,
                        allowLegacy)
                    : allowLegacy && HubSecurity.IsValidSecret(sharedSecret, supplied)
                        ? new DeviceAuthenticationResult(true, true, null, null, "legacy-no-classroom-id")
                        : DeviceAuthenticationResult.Rejected("classroom-id-missing");
                if (!authentication.Success)
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return;
                }
            }
            await next();
        });
        _app.MapHub<TeacherHub>(HubRoutes.TeacherHubPath);

        // Static endpoint untuk file distribution.
        _app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(_files.ShareFolder),
            RequestPath = "/files",
            ServeUnknownFileTypes = true,
            DefaultContentType = "application/octet-stream",
        });

        await _app.StartAsync(cancellationToken);
        _holder.HubContext = _app.Services.GetRequiredService<IHubContext<TeacherHub>>();
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _holder.HubContext = null;
        if (_app is not null)
        {
            await _app.StopAsync(cancellationToken);
            await _app.DisposeAsync();
            _app = null;
        }
    }
}
