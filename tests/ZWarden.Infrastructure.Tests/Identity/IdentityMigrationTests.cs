using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ZWarden.Domain.Tenancy;
using ZWarden.Infrastructure.Identity;
using ZWarden.Infrastructure.Persistence;
using ZWarden.Infrastructure.Security;
using ZWarden.Infrastructure.Tenancy;

namespace ZWarden.Infrastructure.Tests.Identity;

/// <summary>
/// F4 S10: the F4 Identity migration applies through the real migrations pipeline, and the
/// <b>secret-protecting</b> production model (S7's token converter) is migration-consistent — running
/// migrations against it does not raise a pending-model-changes error. Proven on SQLite (offline); the
/// Postgres migration is validated on the networked tier. ADR 0005/0006.
/// </summary>
public class IdentityMigrationTests
{
    [Test]
    public async Task Migrating_the_secured_model_applies_cleanly_and_creates_the_identity_tables()
    {
        string file = Path.Combine(Path.GetTempPath(), $"zw-{Guid.NewGuid():N}.db");
        KeyRing ring = KeyRingLoader.Load($"k1:{Convert.ToBase64String(new byte[KeyRing.KeySizeBytes])}", "k1");

        ServiceCollection services = new();
        services.AddLogging();
        services.AddDataProtection();
        services.AddHttpContextAccessor();
        services.AddSecurityFoundation(ring); // -> the context encrypts token values, so its model differs
        services.AddTenantFoundation();
        services.AddZWardenPersistence(ZWardenDbProvider.Sqlite, $"Data Source={file};Pooling=False");
        services.AddZWardenAuthentication(["localhost"]);

        await using ServiceProvider root = services.BuildServiceProvider();
        try
        {
            // Runs MigrationRunner.MigrateAsync against the real SQLite migrations, then bootstraps the
            // default tenant. MigrateAsync throws PendingModelChangesWarning if the model and the last
            // migration disagree - so a clean run proves the converter added no schema drift.
            await root.MigrateAndBootstrapDefaultTenantAsync();

            using IServiceScope scope = root.CreateScope();
            ZWardenDbContext db = scope.ServiceProvider.GetRequiredService<ZWardenDbContext>();

            // The AspNet* tables exist (querying them would throw otherwise) and the default tenant seeded.
            await Assert.That(await db.Users.CountAsync()).IsEqualTo(0);
            await Assert.That(await db.Roles.CountAsync()).IsEqualTo(0);
            await Assert.That(await db.Set<Tenant>().CountAsync()).IsEqualTo(1);
        }
        finally
        {
            try { File.Delete(file); } catch (IOException) { /* best effort */ }
        }
    }
}
