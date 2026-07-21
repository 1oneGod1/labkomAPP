using System.Threading.Channels;
using LabKom.Data;
using LabKom.Data.Entities;
using LabKom.Shared.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LabKom.Teacher.Services;

/// <summary>
/// Serializes activity writes through one bounded queue so concurrent Hub events
/// cannot contend for SQLite write locks.
/// </summary>
public sealed class PersistenceService : IHostedService
{
    private const int QueueCapacity = 10_000;
    private const int BatchSize = 100;
    private const int MaximumWriteAttempts = 3;

    private readonly PresenceRegistry _presence;
    private readonly ActivityFeed _activity;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PersistenceService> _logger;
    private readonly Channel<PendingActivity> _queue;
    private Task _consumerTask = Task.CompletedTask;
    private long _rejectedEntries;

    public PersistenceService(
        PresenceRegistry presence,
        ActivityFeed activity,
        IServiceScopeFactory scopeFactory,
        ILogger<PersistenceService> logger)
    {
        _presence = presence;
        _activity = activity;
        _scopeFactory = scopeFactory;
        _logger = logger;
        _queue = Channel.CreateBounded<PendingActivity>(
            new BoundedChannelOptions(QueueCapacity)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait,
            });
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LabKomDbContext>();
        await db.Database.EnsureCreatedAsync(cancellationToken);
        _logger.LogInformation(
            "Database SQLite siap: {Source}",
            db.Database.GetConnectionString());

        _consumerTask = ConsumeQueueAsync();
        _presence.PresenceChanged += OnPresenceChanged;
        _presence.ChatReceived += OnChatReceived;
        _activity.RecordReceived += OnActivityReceived;
        _activity.FileProgressReceived += OnFileProgressReceived;
        _activity.CommandResultReceived += OnCommandResultReceived;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _presence.PresenceChanged -= OnPresenceChanged;
        _presence.ChatReceived -= OnChatReceived;
        _activity.RecordReceived -= OnActivityReceived;
        _activity.FileProgressReceived -= OnFileProgressReceived;
        _activity.CommandResultReceived -= OnCommandResultReceived;
        _queue.Writer.TryComplete();

        try
        {
            await _consumerTask.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (
            cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                "Shutdown persistence dibatalkan sebelum queue selesai");
        }
    }

    private void OnPresenceChanged(object? sender, PresenceUpdate update) =>
        Enqueue(new PendingActivity(
            update.PcName,
            ActivityKind.System,
            $"Status: {update.Status}",
            update.Snapshot?.IpAddress,
            FromUnixTime(update.TimestampUnixMs)));

    private void OnChatReceived(object? sender, ChatMessage message) =>
        Enqueue(new PendingActivity(
            message.FromPcName ?? "(broadcast)",
            ActivityKind.ChatSent,
            Limit($"[{message.Direction}] {message.Body}", 256),
            Limit(message.Body, 1_024),
            FromUnixTime(message.TimestampUnixMs)));

    private void OnActivityReceived(object? sender, ActivityRecord record) =>
        Enqueue(new PendingActivity(
            record.PcName,
            MapKind(record),
            Limit(record.Title, 256),
            LimitNullable(BuildDetail(record), 1_024),
            FromUnixTime(record.TimestampUnixMs)));

    private void OnFileProgressReceived(
        object? sender,
        FileDistributionProgress progress) =>
        Enqueue(new PendingActivity(
            progress.PcName,
            ActivityKind.FileTransfer,
            Limit(
                $"Transfer file: {progress.State} " +
                $"({progress.BytesReceived} byte)",
                256),
            Limit(
                $"{progress.NoticeId} | {progress.ErrorMessage}",
                1_024),
            FromUnixTime(progress.TimestampUnixMs)));

    private void OnCommandResultReceived(
        object? sender,
        CommandResult result) =>
        Enqueue(new PendingActivity(
            result.PcName,
            ActivityKind.System,
            Limit($"{result.Kind}: {result.State}", 256),
            Limit($"{result.CommandId} | {result.Message}", 1_024),
            FromUnixTime(result.TimestampUnixMs)));

    private void Enqueue(PendingActivity entry)
    {
        if (_queue.Writer.TryWrite(entry)) return;

        var rejected = Interlocked.Increment(ref _rejectedEntries);
        if (rejected == 1 || rejected % 100 == 0)
        {
            _logger.LogError(
                "Queue audit penuh; {Count} entry ditolak",
                rejected);
        }
    }

    private async Task ConsumeQueueAsync()
    {
        var batch = new List<PendingActivity>(BatchSize);
        while (await _queue.Reader.WaitToReadAsync())
        {
            batch.Clear();
            while (batch.Count < BatchSize
                   && _queue.Reader.TryRead(out var entry))
            {
                batch.Add(entry);
            }

            if (batch.Count > 0)
            {
                await PersistBatchAsync(batch);
            }
        }
    }

    private async Task PersistBatchAsync(
        IReadOnlyCollection<PendingActivity> batch)
    {
        for (var attempt = 1; attempt <= MaximumWriteAttempts; attempt++)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider
                    .GetRequiredService<LabKomDbContext>();
                db.Activities.AddRange(batch.Select(entry => new ActivityLog
                {
                    PcName = entry.PcName,
                    Kind = entry.Kind,
                    Title = entry.Title,
                    Detail = entry.Detail,
                    At = entry.AtUtc,
                }));
                await db.SaveChangesAsync();
                return;
            }
            catch (Exception ex) when (attempt < MaximumWriteAttempts)
            {
                _logger.LogWarning(
                    ex,
                    "Write audit gagal pada percobaan {Attempt}; retry",
                    attempt);
                await Task.Delay(TimeSpan.FromMilliseconds(100 * attempt));
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "{Count} entry audit gagal disimpan setelah retry",
                    batch.Count);
            }
        }
    }

    private static DateTime FromUnixTime(long timestampUnixMs)
    {
        try
        {
            return DateTimeOffset
                .FromUnixTimeMilliseconds(timestampUnixMs)
                .UtcDateTime;
        }
        catch (ArgumentOutOfRangeException)
        {
            return DateTime.UtcNow;
        }
    }

    private static string Limit(string value, int maximumLength) =>
        value.Length <= maximumLength ? value : value[..maximumLength];

    private static string? LimitNullable(string? value, int maximumLength) =>
        value is null ? null : Limit(value, maximumLength);

    private static string? BuildDetail(ActivityRecord record)
    {
        if (record.Metrics is null) return record.ProcessName;
        return $"{record.ProcessName} | {record.Metrics.Category} | " +
               $"keyboard-events={record.Metrics.KeyboardEventCount} | " +
               $"idle-ms={record.Metrics.IdleMilliseconds}";
    }

    private static ActivityKind MapKind(ActivityRecord record) =>
        record.Kind switch
        {
            ActivityRecordKind.WindowChange => ActivityKind.WindowChange,
            ActivityRecordKind.ProcessStart => ActivityKind.AppLaunched,
            ActivityRecordKind.ProcessStop => ActivityKind.AppClosed,
            ActivityRecordKind.UsageSample
                when record.Metrics?.Category
                    == ActivityCategory.WebBrowser =>
                ActivityKind.BrowserUrl,
            ActivityRecordKind.UsageSample => ActivityKind.UsageSample,
            ActivityRecordKind.FileCollected => ActivityKind.FileCollection,
            ActivityRecordKind.Registration => ActivityKind.Registration,
            ActivityRecordKind.Assessment => ActivityKind.Assessment,
            _ => ActivityKind.System,
        };

    private sealed record PendingActivity(
        string PcName,
        ActivityKind Kind,
        string Title,
        string? Detail,
        DateTime AtUtc);
}