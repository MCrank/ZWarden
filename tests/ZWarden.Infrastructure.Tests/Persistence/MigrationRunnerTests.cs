using Microsoft.EntityFrameworkCore;
using ZWarden.Infrastructure.Persistence;
using ZWarden.TestSupport;

namespace ZWarden.Infrastructure.Tests.Persistence;

/// <summary>S3: the migrate-on-startup runner respects its opt-out.</summary>
public class MigrationRunnerTests
{
    [Test]
    public async Task Opt_out_does_not_touch_the_database()
    {
        string file = Path.Combine(Path.GetTempPath(), $"zw-{Guid.NewGuid():N}.db");
        DbContextOptions options = new DbContextOptionsBuilder<TestDbContext>()
            .UseZWardenProvider(ZWardenDbProvider.Sqlite, $"Data Source={file};Pooling=False")
            .Options;
        try
        {
            await using TestDbContext ctx = new(options);
            await MigrationRunner.EnsureMigratedAsync(ctx, enabled: false);

            // Disabled: nothing ran, so the database file was never created.
            await Assert.That(File.Exists(file)).IsFalse();
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
