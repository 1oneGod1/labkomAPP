using LabKom.Shared.Hub;
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
    private readonly HubContextHolder _holder;
    private readonly FileDistributionService _files;
    private readonly ActivityFeed _feed;
    private readonly ILoggerFactory _loggerFactory;
    private WebApplication? _app;

    public HubHostService(
        IConfiguration config,
        PresenceRegistry registry,
        HubContextHolder holder,
        FileDistributionService files,
        ActivityFeed feed,
        ILoggerFactory loggerFactory)
    {
        _config = config;
        _registry = registry;
        _holder = holder;
        _files = files;
        _feed = feed;
        _loggerFactory = loggerFactory;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var port = _config.GetValue("Teacher:HubPort", 41235);
        var sharedSecret = Environment.GetEnvironmentVariable("LABKOM_SHARED_SECRET")
                           ?? _config["Teacher:SharedSecret"]
                           ?? string.Empty;
        if (sharedSecret.Length < 16)
        {
            throw new InvalidOperationException("LABKOM_SHARED_SECRET/Teacher:SharedSecret wajib diisi minimal 16 karakter.");
        }

        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(_loggerFactory);
        builder.Services.AddSingleton(_config);
        builder.Services.AddSingleton(_registry);
        builder.Services.AddSingleton(_feed);
        builder.Services.AddSignalR(options =>
        {
            options.EnableDetailedErrors = false;
            options.MaximumReceiveMessageSize = 4 * 1024 * 1024;
        });

        builder.WebHost.ConfigureKestrel(opt =>
        {
            opt.ListenAnyIP(port);
            // File besar bisa di-upload via HTTP (file distribution dari guru → siswa).
            opt.Limits.MaxRequestBodySize = 200 * 1024 * 1024; // 200MB
        });

        _app = builder.Build();
        _app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/files"))
            {
                var supplied = context.Request.Headers[HubSecurity.HeaderName].ToString();
                if (!HubSecurity.IsValidSecret(sharedSecret, supplied))
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
