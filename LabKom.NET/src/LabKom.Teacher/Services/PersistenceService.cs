using LabKom.Data;
using LabKom.Data.Entities;
using LabKom.Shared.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LabKom.Teacher.Services;

/// <summary>
/// Subscribe ke event PresenceRegistry & ActivityFeed lalu persist ke SQLite.
/// Dipanggil sebagai IHostedService supaya start otomatis bersama host WPF.
/// </summary>
public class PersistenceService : IHostedService
{
    private readonly PresenceRegistry _presence;
    private readonly ActivityFeed _activity;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PersistenceService> _logger;

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
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // Pastikan database & skema ada (no migrations needed di MVP).
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LabKomDbContext>();
        await db.Database.EnsureCreatedAsync(cancellationToken);
        _logger.LogInformation("Database SQLite siap: {Source}", db.Database.GetConnectionString());

        _presence.PresenceChanged += OnPresenceChanged;
        _presence.ChatReceived += OnChatReceived;
        _activity.RecordReceived += OnActivityReceived;
        _activity.CommandResultReceived += OnCommandResultReceived;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _presence.PresenceChanged -= OnPresenceChanged;
        _presence.ChatReceived -= OnChatReceived;
        _activity.RecordReceived -= OnActivityReceived;
        _activity.CommandResultReceived -= OnCommandResultReceived;
        return Task.CompletedTask;
    }

    private void OnPresenceChanged(object? sender, PresenceUpdate u) =>
        _ = PersistPresenceAsync(u);

    private void OnChatReceived(object? sender, ChatMessage m) =>
        _ = PersistChatAsync(m);

    private void OnActivityReceived(object? sender, ActivityRecord r) =>
        _ = PersistActivityAsync(r);

    private void OnCommandResultReceived(object? sender, CommandResult result) =>
        _ = PersistCommandResultAsync(result);

    private async Task PersistPresenceAsync(PresenceUpdate update)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<LabKomDbContext>();

            // MVP: log ke ActivityLog dengan kind System.
            db.Activities.Add(new ActivityLog
            {
                PcName = update.PcName,
                Kind = ActivityKind.System,
                Title = $"Status: {update.Status}",
                Detail = update.Snapshot?.IpAddress,
            });
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Gagal persist presence");
        }
    }

    private async Task PersistChatAsync(ChatMessage msg)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<LabKomDbContext>();
            db.Activities.Add(new ActivityLog
            {
                PcName = msg.FromPcName ?? "(broadcast)",
                Kind = ActivityKind.ChatSent,
                Title = Limit($"[{msg.Direction}] {msg.Body}", 256),
                Detail = Limit(msg.Body, 1_024),
            });
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Gagal persist chat");
        }
    }

    private async Task PersistCommandResultAsync(CommandResult result)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<LabKomDbContext>();
            db.Activities.Add(new ActivityLog
            {
                PcName = result.PcName,
                Kind = ActivityKind.System,
                Title = Limit($"{result.Kind}: {result.State}", 256),
                Detail = Limit($"{result.CommandId} | {result.Message}", 1_024),
            });
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Gagal persist command result");
        }
    }

    private async Task PersistActivityAsync(ActivityRecord r)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<LabKomDbContext>();
            db.Activities.Add(new ActivityLog
            {
                PcName = r.PcName,
                Kind = MapKind(r.Kind),
                Title = Limit(r.Title, 256),
                Detail = LimitNullable(r.ProcessName, 1_024),
            });
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Gagal persist activity");
        }
    }

    private static string Limit(string value, int maximumLength) =>
        value.Length <= maximumLength ? value : value[..maximumLength];

    private static string? LimitNullable(string? value, int maximumLength) =>
        value is null ? null : Limit(value, maximumLength);

    private static ActivityKind MapKind(ActivityRecordKind k) => k switch
    {
        ActivityRecordKind.WindowChange => ActivityKind.WindowChange,
        ActivityRecordKind.ProcessStart => ActivityKind.AppLaunched,
        ActivityRecordKind.ProcessStop => ActivityKind.AppClosed,
        _ => ActivityKind.System,
    };
}
