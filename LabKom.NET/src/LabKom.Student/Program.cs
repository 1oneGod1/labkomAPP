using LabKom.Shared.Devices;
using LabKom.Shared.Discovery;
using LabKom.Shared.Hub;
using LabKom.Shared.Security;
using LabKom.Shared.Updates;
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

var sharedSecret = ProvisionedSecretStore.Resolve(builder.Configuration["Agent:SharedSecret"]);
if (!HubSecurity.IsStrongSecret(sharedSecret))
{
    throw new InvalidOperationException(
        $"Secret LabKom wajib diprovisioning atau diisi minimal {HubSecurity.MinimumSecretLength} karakter.");
}
builder.Configuration["Agent:SharedSecret"] = sharedSecret;
if (ProvisionedSecretStore.TryRead(null, out var studentProvisioning))
{
    _ = DeviceCredentialStore.EnsureFromProvisioning(
        studentProvisioning,
        Environment.MachineName);
    if (builder.Configuration.GetValue(
            "Security:RestrictBootstrapSecret",
            false))
        ProvisionedSecretStore.RestrictToAdministrators();
}

builder.Services.AddWindowsService(options => options.ServiceName = "LabKomStudentAgent");



// Singleton state
builder.Services.AddSingleton<MachineIdentity>();
builder.Services.AddSingleton<TeacherEndpointStore>();
builder.Services.AddSingleton<PowerService>();
builder.Services.AddSingleton<DiscoveryClient>();
builder.Services.AddSingleton<WebFilterEnforcer>();
builder.Services.AddSingleton<AppBlockEnforcer>();
builder.Services.AddSingleton<DeviceTelemetryCollector>();
builder.Services.AddSingleton<HubConnectionService>();

// Background workers
builder.Services.AddHostedService<DiscoveryWorker>();
builder.Services.AddHostedService<AgentWorker>();
builder.Services.AddHostedService<AppBlockWorker>();
builder.Services.AddHostedService<StudentDesktopWatchdog>();

var host = builder.Build();
await host.StartAsync();
UpdateHealthReporter.MarkHealthy(
    "Student",
    typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "0.0.0");

if (args.Contains("--health-check", StringComparer.OrdinalIgnoreCase))
{
    await host.StopAsync(TimeSpan.FromSeconds(5));
    return;
}

if (!WindowsServiceHelpers.IsWindowsService())
{
    Console.WriteLine("[LabKom.Student] Console mode. Workers: discovery, agent, app-policy. Ctrl+C untuk keluar.");
}
await host.WaitForShutdownAsync();
