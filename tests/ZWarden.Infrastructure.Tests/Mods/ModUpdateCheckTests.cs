using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ZWarden.Application.Mods;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Mods;
using ZWarden.Infrastructure.Mods;
using ZWarden.Infrastructure.Persistence;
using ZWarden.TestSupport;

namespace ZWarden.Infrastructure.Tests.Mods;

/// <summary>
/// #275 D4: the hourly mod-update check. A server that just sits running is never rediscovered, so nothing else would
/// refresh its items' Steam <c>time_updated</c>; the check queues one metadata refresh per Server that has Workshop
/// files on disk, with the short update-check max age. Real SQLite.
/// </summary>
public class ModUpdateCheckTests
{
    private static readonly TenantId Tenant = TenantId.New();

    [Test]
    public async Task It_queues_one_short_max_age_refresh_per_server_with_files_on_disk()
    {
        ServerId withFiles = ServerId.New();
        ServerId nothingOnDisk = ServerId.New();
        await WithSqlite(async options =>
        {
            await using (ZWardenDbContext seed = new(options, new TestTenantContext(Tenant)))
            {
                seed.AddRange(OnDisk(withFiles, "100"), OnDisk(withFiles, "200"), ServerWorkshopItem.Track(nothingOnDisk, "300"));
                await seed.SaveChangesAsync();
            }

            ModStateRecorderTests.RecordingQueue queue = new();
            ModRefreshOptions refresh = new();
            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            int queued = await new ModUpdateCheck(
                new ServerWorkshopItemRepository(db), queue, new TestTenantContext(Tenant), Options.Create(refresh)).RunAsync();

            await Assert.That(queued).IsEqualTo(1);
            await Assert.That(queue.Requests).IsEquivalentTo(
                [new ModRefreshRequest(Tenant, ModRefreshKind.RefreshMetadata, Server: withFiles, MaxAge: refresh.UpdateCheckMaxAge)]);
        });
    }

    [Test]
    public async Task The_update_check_max_age_is_shorter_than_its_interval()
    {
        // A refresh exactly one interval ago must count as due on the next tick, or the cadence silently doubles.
        ModRefreshOptions defaults = new();

        await Assert.That(defaults.UpdateCheckInterval).IsEqualTo(TimeSpan.FromHours(1));
        await Assert.That(defaults.UpdateCheckMaxAge).IsLessThan(defaults.UpdateCheckInterval);
    }

    private static ServerWorkshopItem OnDisk(ServerId server, string workshopId)
    {
        ServerWorkshopItem item = ServerWorkshopItem.Track(server, workshopId);
        item.ObserveDisk(onDisk: true, [], DateTimeOffset.UnixEpoch);
        return item;
    }

    private static async Task WithSqlite(Func<DbContextOptions, Task> body)
    {
        string file = Path.Combine(Path.GetTempPath(), $"zw-{Guid.NewGuid():N}.db");
        DbContextOptions options = new DbContextOptionsBuilder<ZWardenDbContext>()
            .UseZWardenProvider(ZWardenDbProvider.Sqlite, $"Data Source={file};Pooling=False")
            .Options;
        try
        {
            await using (ZWardenDbContext db = new(options, new TestTenantContext(Tenant)))
            {
                await db.Database.EnsureCreatedAsync();
            }

            await body(options);
        }
        finally
        {
            try
            {
                File.Delete(file);
            }
            catch (IOException)
            {
            }
        }
    }
}
