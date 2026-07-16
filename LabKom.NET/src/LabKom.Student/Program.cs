using LabKom.Shared.Devices;
using LabKom.Shared.Discovery;
using LabKom.Shared.Hub;
using LabKom.Student.Services;
using LabKom.Student.Workers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.WindowsServices;

var options = new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
};

var builder = Host.CreateApplicationBuilder(options);

builder.Configuration
    .AddJsonFile("appsettings.Local.json", optional: true)
    .AddEnvironmentVariables();

var sharedSecret = Environment.GetEnvironmentVariable("LABKOM_SHARED_SECRET")
                   ?? builder.Configuration["Agent:SharedSecret"];
if (!HubSecurity.IsStrongSecret(sharedSecret))
{
    throw new InvalidOperationException(
        $"LABKOM_SHARED_SECRET wajib diisi minimal {HubSecurity.MinimumSecretLength} karakter.");
}

builder.Services.AddWindowsService(options => options.ServiceName = "LabKomStudentAgent");



// Singleton state
builder.Services.AddSingleton<MachineIdentity>();
builder.Services.AddSingleton<TeacherEndpointStore>();
builder.Services.AddSingleton<PowerService>();
builder.Services.AddSingleton<DiscoveryClient>();
builder.Services.AddSingleton<WebFilterEnforcer>();
builder.Services.AddSingleton<AppBlockEnforcer>();
builder.Services.AddSingleton<HubConnectionService>();

// Background workers
builder.Services.AddHostedService<DiscoveryWorker>();
builder.Services.AddHostedService<AgentWorker>();
builder.Services.AddHostedService<AppBlockWorker>();

var host = builder.Build();

if (WindowsServiceHelpers.IsWindowsService())
{
    await host.RunAsync();
}
else
{
    Console.WriteLine("[LabKom.Student] Console mode. Workers: discovery, agent, app-policy. Ctrl+C untuk keluar.");
    await host.RunAsync();
}
