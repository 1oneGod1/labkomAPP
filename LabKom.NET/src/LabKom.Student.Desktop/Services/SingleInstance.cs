using System.Threading;

namespace LabKom.Student.Desktop.Services;

/// <summary>
/// Mutex global untuk mencegah dua instance Student Desktop berjalan
/// ketika user login dan auto-start race condition.
/// </summary>
public static class SingleInstance
{
    private static Mutex? _mutex;

    public static bool TryAcquire(string name)
    {
        _mutex = new Mutex(initiallyOwned: true, name, out var createdNew);
        return createdNew;
    }
}
