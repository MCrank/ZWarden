using Microsoft.EntityFrameworkCore;
using ZWarden.Domain.Ids;
using ZWarden.Infrastructure.Persistence;

namespace ZWarden.Infrastructure.Tests.Persistence;

/// <summary>S2: the SQLite hardening and version-stamping interceptors (offline).</summary>
public class InterceptorSqliteTests
{
    private static (DbContextOptions Options, string File) NewSqlite()
    {
        string file = Path.Combine(Path.GetTempPath(), $"zw-{Guid.NewGuid():N}.db");
        DbContextOptions options = new DbContextOptionsBuilder<TestDbContext>()
            .UseZWardenProvider(ZWardenDbProvider.Sqlite, $"Data Source={file};Pooling=False")
            .Options;
        return (options, file);
    }

    private static void Cleanup(string file)
    {
        try
        {
            File.Delete(file);
        }
        catch (IOException)
        {
        }
    }

    [Test]
    public async Task Sqlite_connection_is_hardened_on_open()
    {
        (DbContextOptions options, string file) = NewSqlite();
        try
        {
            await using TestDbContext ctx = new(options);
            await Assert.That(ctx.Database.GetCommandTimeout()).IsEqualTo(30);

            await ctx.Database.OpenConnectionAsync();
            var connection = ctx.Database.GetDbConnection();

            async Task<string> Pragma(string name)
            {
                await using var command = connection.CreateCommand();
                command.CommandText = $"PRAGMA {name};";
                return (await command.ExecuteScalarAsync())!.ToString()!;
            }

            await Assert.That(await Pragma("journal_mode")).IsEqualTo("wal");
            await Assert.That(await Pragma("foreign_keys")).IsEqualTo("1");
            await Assert.That(await Pragma("busy_timeout")).IsEqualTo("5000");

            await ctx.Database.CloseConnectionAsync();
        }
        finally
        {
            Cleanup(file);
        }
    }

    [Test]
    public async Task Version_is_stamped_on_insert_and_changes_on_update()
    {
        (DbContextOptions options, string file) = NewSqlite();
        try
        {
            await using TestDbContext ctx = new(options);
            await ctx.Database.EnsureCreatedAsync();

            Widget widget = new() { Id = ServerId.New(), Name = "a" };
            ctx.Add(widget);
            await ctx.SaveChangesAsync();

            Guid afterInsert = widget.Version;
            await Assert.That(afterInsert).IsNotEqualTo(Guid.Empty);

            widget.Name = "b";
            await ctx.SaveChangesAsync();
            await Assert.That(widget.Version).IsNotEqualTo(afterInsert);
        }
        finally
        {
            Cleanup(file);
        }
    }

    [Test]
    public async Task A_stale_update_raises_a_concurrency_conflict()
    {
        (DbContextOptions options, string file) = NewSqlite();
        try
        {
            ServerId id = ServerId.New();
            await using (TestDbContext seed = new(options))
            {
                await seed.Database.EnsureCreatedAsync();
                seed.Add(new Widget { Id = id, Name = "v1" });
                await seed.SaveChangesAsync();
            }

            await using TestDbContext first = new(options);
            await using TestDbContext second = new(options);
            Widget fromFirst = await first.Widgets.SingleAsync();
            Widget fromSecond = await second.Widgets.SingleAsync();

            fromFirst.Name = "from-first";
            await first.SaveChangesAsync();

            fromSecond.Name = "from-second";
            await Assert.That(async () => await second.SaveChangesAsync())
                .Throws<DbUpdateConcurrencyException>();
        }
        finally
        {
            Cleanup(file);
        }
    }
}
