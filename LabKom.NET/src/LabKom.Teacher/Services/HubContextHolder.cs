using LabKom.Teacher.Hub;
using Microsoft.AspNetCore.SignalR;

namespace LabKom.Teacher.Services;

/// <summary>
/// Jembatan antara DI utama Teacher Console (WPF) dan DI internal
/// WebApplication SignalR. HubHostService meng-isi HubContext setelah
/// WebApplication.Start, RemoteCommandService konsumsi via property ini.
/// </summary>
public class HubContextHolder
{
    public IHubContext<TeacherHub>? HubContext { get; set; }
}
