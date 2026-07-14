using System.Collections.Concurrent;
using LabKom.Shared.Contracts;

namespace LabKom.Teacher.Services;

/// <summary>
/// Buffer in-memory dari activity records terbaru, plus event RecordReceived
/// supaya UI dan PersistenceService bisa subscribe.
/// </summary>
public class ActivityFeed
{
    private const int MaxRecords = 500;
    private readonly ConcurrentQueue<ActivityRecord> _buffer = new();

    public event EventHandler<ActivityRecord>? RecordReceived;
    public event EventHandler<FileDistributionProgress>? FileProgressReceived;

    public void Push(ActivityRecord record)
    {
        _buffer.Enqueue(record);
        while (_buffer.Count > MaxRecords && _buffer.TryDequeue(out _)) { }
        RecordReceived?.Invoke(this, record);
    }

    public void PushFileProgress(FileDistributionProgress progress)
    {
        FileProgressReceived?.Invoke(this, progress);
    }

    public IReadOnlyCollection<ActivityRecord> Recent() => _buffer.ToArray();
}
