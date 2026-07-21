using LabKom.Data;
using LabKom.Shared.Contracts;
using LabKom.Teacher.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LabKom.Tests;

public sealed class PersistenceQueueTests
{
    [Fact]
    public async Task ConcurrentActivityEventsAreSerializedAndDrainedOnStop()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "LabKom.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var databasePath = Path.Combine(root, "audit.db");
        ServiceProvider? provider = null;

        try
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDbContext<LabKomDbContext>(options =>
                options.UseSqlite(
                    $"Data Source={databasePath};Pooling=False"));
            provider = services.BuildServiceProvider();

            var presence = new PresenceRegistry();
            var feed = new ActivityFeed();
            var persistence = new PersistenceService(
                presence,
                feed,
                provider.GetRequiredService<IServiceScopeFactory>(),
                provider.GetRequiredService<ILogger<PersistenceService>>());
            await persistence.StartAsync(
                TestContext.Current.CancellationToken);

            const int expected = 500;
            Parallel.For(0, expected, index =>
                feed.Push(new ActivityRecord(
                    $"PC-{index % 20:00}",
                    ActivityRecordKind.WindowChange,
                    $"Window {index}",
                    "test",
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())));

            await persistence.StopAsync(
                TestContext.Current.CancellationToken);

            await using (var scope = provider.CreateAsyncScope())
            {
                var db = scope.ServiceProvider
                    .GetRequiredService<LabKomDbContext>();
                Assert.Equal(
                    expected,
                    await db.Activities.CountAsync(
                        TestContext.Current.CancellationToken));
            }
        }
        finally
        {
            if (provider is not null)
            {
                await provider.DisposeAsync();
            }

            SafeDelete(root);
        }
    }

    private static void SafeDelete(string root)
    {
        var fullRoot = Path.GetFullPath(root);
        var tempRoot = Path.GetFullPath(Path.GetTempPath());
        if (fullRoot.StartsWith(
                tempRoot,
                StringComparison.OrdinalIgnoreCase)
            && Directory.Exists(fullRoot))
        {
            Directory.Delete(fullRoot, recursive: true);
        }
    }
}