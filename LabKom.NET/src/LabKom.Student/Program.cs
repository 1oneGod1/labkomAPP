using LabKom.Student.Services;
using LabKom.Student.Services.Capture;
using LabKom.Student.Workers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.WindowsServices;

var options = new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
};

var builder = Host.CreateApplicationBuilder(options);

builder.Services.AddWindowsService(o => { o.ServiceName = "LabKomStudentAgent"; });

builder.Services.AddHttpClient<FileDownloader>();

// Singleton state
builder.Services.AddSingleton<MachineIdentity>();
builder.Services.AddSingleton<TeacherEndpointStore>();
builder.Services.AddSingleton<CaptureProfileState>();
builder.Services.AddSingleton<PowerService>();
builder.Services.AddSingleton<DiscoveryClient>();
builder.Services.AddSingleton<IScreenCaptureSource, GdiScreenCapture>();
builder.Services.AddSingleton<ActivityMonitor>();
builder.Services.AddSingleton<WebFilterEnforcer>();
builder.Services.AddSingleton<AppBlockEnforcer>();
builder.Services.AddSingleton<HubConnectionService>();

// Background workers
builder.Services.AddHostedService<DiscoveryWorker>();
builder.Services.AddHostedService<AgentWorker>();
builder.Services.AddHostedService<ScreenStreamWorker>();
builder.Services.AddHostedService<ActivityWorker>();
builder.Services.AddHostedService<AppBlockWorker>();

var host = builder.Build();

if (WindowsServiceHelpers.IsWindowsService())
{
    await host.RunAsync();
}
else
{
    Console.WriteLine("[LabKom.Student] Console mode. Workers: discovery, agent, screen, activity. Ctrl+C untuk keluar.");
    await host.RunAsync();
}
