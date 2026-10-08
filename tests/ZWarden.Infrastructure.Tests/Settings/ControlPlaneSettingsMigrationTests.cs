using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ZWarden.Domain.Ids;
using ZWarden.Infrastructure.Persistence;
using ZWarden.TestSupport;

namespace ZWarden.Infrastructure.Tests.Settings;

/// <summary>
/// #345: an install whose built-in roles were seeded before <c>Tenant.Settings.Manage</c> existed gets it on its Tenant
/// Owner and Administrator roles from the <c>AddControlPlaneSettings</c> migration, once, and no other role does.
/// Proven against the real SQLite migrations.
/// </summary>
public sealed class ControlPlaneSettingsMigrationTests
{
    private const string BeforeSettings = "20261003225155_AddWorkshopItemInstalledUpdatedAt";

    [Test]
    public async Task The_migration_grants_the_new_permission_to_existing_owner_and_administrator_roles_only()
    {
        string file = Path.Combine(Path.GetTempPath(), $"zw-{Guid.NewGuid():N}.db");
        TenantId tenant = TenantId.New();
        DbContextOptions options = new DbContextOptionsBuilder<ZWardenDbContext>()
            .UseZWardenProvider(ZWardenDbProvider.Sqlite, $"Data Source={file};Pooling=False")
            .Options;
        try
        {
            await using ZWardenDbContext db = new(options, new TestTenantContext(tenant));
            IMigrator migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync(BeforeSettings);

            Guid owner = await SeedRoleAsync(db, tenant, "TenantOwner");
            Guid admin = await SeedRoleAsync(db, tenant, "Administrator");
            Guid viewer = await SeedRoleAsync(db, tenant, "Viewer");

            await migrator.MigrateAsync();

            await Assert.That(await GrantsAsync(db, owner)).IsEqualTo(1);
            await Assert.That(await GrantsAsync(db, admin)).IsEqualTo(1);
            await Assert.That(await GrantsAsync(db, viewer)).IsEqualTo(0);
        }
        finally
        {
            try { File.Delete(file); } catch (IOException) { /* best effort */ }
        }
    }

    private static async Task<Guid> SeedRoleAsync(ZWardenDbContext db, TenantId tenant, string builtIn)
    {
        Guid id = Guid.NewGuid();
        Guid tenantId = tenant.Value;
        Guid version = Guid.NewGuid();
        await db.Database.ExecuteSqlAsync(
            $"""INSERT INTO "Roles" ("Id", "BuiltIn", "Name", "TenantId", "Version") VALUES ({id}, {builtIn}, {builtIn}, {tenantId}, {version})""");
        return id;
    }

    private static async Task<int> GrantsAsync(ZWardenDbContext db, Guid role) =>
        await db.Database
            .SqlQuery<int>($"""SELECT COUNT(*) AS "Value" FROM "RolePermissionGrants" WHERE "RoleId" = {role} AND "PermissionName" = 'Tenant.Settings.Manage'""")
            .SingleAsync();
}
