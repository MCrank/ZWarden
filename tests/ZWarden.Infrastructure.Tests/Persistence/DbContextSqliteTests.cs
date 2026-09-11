using Microsoft.EntityFrameworkCore;
using ZWarden.Domain.Ids;
using ZWarden.Infrastructure.Persistence;

namespace ZWarden.Infrastructure.Tests.Persistence;

/// <summary>S1: the context builds on SQLite and the typed-id convention round-trips (offline).</summary>
public class DbContextSqliteTests
{
    [Test]
    public async Task Typed_id_key_round_trips_through_a_real_sqlite_database()
    {
        string file = Path.Combine(Path.GetTempPath(), $"zw-{Guid.NewGuid():N}.db");
        DbContextOptions options = new DbContextOptionsBuilder<TestDbContext>()
            .UseZWardenProvider(ZWardenDbProvider.Sqlite, $"Data Source={file};Pooling=False")
            .Options;
        ServerId id = ServerId.New();

        try
        {
            await using (TestDbContext ctx = new(options))
            {
                await ctx.Database.EnsureCreatedAsync();
                ctx.Widgets.Add(new Widget { Id = id, Name = "alpha" });
                await ctx.SaveChangesAsync();
            }

            await using (TestDbContext ctx = new(options))
            {
                Widget widget = await ctx.Widgets.SingleAsync();
                await Assert.That(widget.Id).IsEqualTo(id);
                await Assert.That(widget.Name).IsEqualTo("alpha");
            }
        }
        finally
        {
            try
            {
                File.Delete(file);
            }
            catch (IOException)
            {
                // Best effort - the temp file is harmless if a pooled handle lingers.
            }
        }
    }

    [Test]
    public async Task Provider_parsing_is_case_insensitive_and_fails_fast()
    {
        await Assert.That(ZWardenDbProviderExtensions.ParseProvider("SQLite")).IsEqualTo(ZWardenDbProvider.Sqlite);
        await Assert.That(ZWardenDbProviderExtensions.ParseProvider("postgresql")).IsEqualTo(ZWardenDbProvider.Postgres);
        await Assert.That(() => ZWardenDbProviderExtensions.ParseProvider("mysql")).Throws<ArgumentException>();
    }
}
