using System.Threading;

namespace LabKom.Student.Overlay.Services;

/// <summary>
/// Mutex global untuk mencegah dua instance overlay berjalan
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
