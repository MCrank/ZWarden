using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ZWarden.Application.Tenancy;
using ZWarden.Infrastructure.Persistence;

namespace ZWarden.Infrastructure.Tests.Persistence;

/// <summary>
/// F40 release gate — the migration/upgrade drill (PRD §59). Two providers ship in v1.0 (ADR 0005), so both
/// migration chains must stay in lockstep with the model and must apply cleanly to a fresh database. These are
/// offline: model-drift is a comparison between the current EF model and the snapshot baked into each migrations
/// assembly (no connection), and the SQLite chain applies against a throwaway file. The Postgres chain applying
/// end to end against a live server is covered on the networked tier (PostgresTenantTests et al.).
/// </summary>
public class MigrationDriftTests
{
    [Test]
    public async Task The_sqlite_model_has_no_pending_changes_against_its_migrations()
    {
        await using ZWardenDbContext context = Context(ZWardenDbProvider.Sqlite, "Data Source=:memory:");

        // A model edited without a matching migration would make this true — and MigrateAsync would then throw a
        // PendingModelChangesWarning at deploy time. Catch it here, on every PR, instead.
        await Assert.That(context.Database.HasPendingModelChanges()).IsFalse();
    }

    [Test]
    public async Task The_postgres_model_has_no_pending_changes_against_its_migrations()
    {
        await using ZWardenDbContext context = Context(
            ZWardenDbProvider.Postgres, "Host=localhost;Database=unused;Username=unused;Password=unused");

        await Assert.That(context.Database.HasPendingModelChanges()).IsFalse();
    }

    [Test]
    public async Task The_full_sqlite_migration_chain_applies_to_an_empty_database()
    {
        string file = TempDbFile();
        try
        {
            await using ZWardenDbContext context = Context(ZWardenDbProvider.Sqlite, $"Data Source={file};Pooling=False");

            // A fresh install: apply every migration from empty.
            await context.Database.MigrateAsync();

            await Assert.That(await context.Database.GetPendingMigrationsAsync()).IsEmpty();
            await Assert.That((await context.Database.GetAppliedMigrationsAsync()).Any()).IsTrue();
        }
        finally
        {
            TryDelete(file);
        }
    }

    [Test]
    public async Task The_latest_sqlite_migration_rolls_back_and_forward_again()
    {
        string file = TempDbFile();
        try
        {
            await using ZWardenDbContext context = Context(ZWardenDbProvider.Sqlite, $"Data Source={file};Pooling=False");
            List<string> chain = context.Database.GetMigrations().ToList();
            await Assert.That(chain.Count).IsGreaterThan(1);
            string head = chain[^1];
            string previous = chain[^2];
            IMigrator migrator = context.GetService<IMigrator>();

            await migrator.MigrateAsync();                 // up to head (the upgrade path)
            await migrator.MigrateAsync(previous);         // roll the latest migration back (the rollback path)
            await Assert.That(await context.Database.GetAppliedMigrationsAsync()).DoesNotContain(head);

            await migrator.MigrateAsync();                 // and forward again
            await Assert.That(await context.Database.GetPendingMigrationsAsync()).IsEmpty();
        }
        finally
        {
            TryDelete(file);
        }
    }

    [Test]
    public async Task Upgrading_grants_server_recreate_to_existing_owner_and_administrator_roles_only()
    {
        // #229: built-in roles are seeded once and their grants persisted, so an install seeded before Server.Recreate
        // existed only gets it from the data migration — and only on the two roles whose bundle includes it.
        string file = TempDbFile();
        try
        {
            await using ZWardenDbContext context = Context(ZWardenDbProvider.Sqlite, $"Data Source={file};Pooling=False");
            List<string> chain = context.Database.GetMigrations().ToList();
            string grant = chain.Single(m => m.EndsWith("_GrantServerRecreateToBuiltInRoles", StringComparison.Ordinal));
            string beforeGrant = chain[chain.IndexOf(grant) - 1];
            IMigrator migrator = context.GetService<IMigrator>();

            await migrator.MigrateAsync(beforeGrant);
            foreach (string kind in new[] { "TenantOwner", "Administrator", "Operator" })
            {
                await context.Database.ExecuteSqlAsync(
                    $"""INSERT INTO "Roles" ("Id", "TenantId", "Name", "BuiltIn", "Version") VALUES ({Guid.NewGuid().ToString().ToUpperInvariant()}, {Guid.NewGuid().ToString().ToUpperInvariant()}, {kind}, {kind}, {Guid.NewGuid().ToString().ToUpperInvariant()})""");
            }

            await migrator.MigrateAsync();

            List<string> granted = await context.Database
                .SqlQuery<string>(
                    $"""SELECT r."BuiltIn" AS "Value" FROM "RolePermissionGrants" g JOIN "Roles" r ON r."Id" = g."RoleId" WHERE g."PermissionName" = 'Server.Recreate'""")
                .ToListAsync();
            granted.Sort(StringComparer.Ordinal);
            string[] expected = ["Administrator", "TenantOwner"];
            await Assert.That(granted).IsEquivalentTo(expected);
        }
        finally
        {
            TryDelete(file);
        }
    }

    private static ZWardenDbContext Context(ZWardenDbProvider provider, string connectionString)
    {
        DbContextOptions options = new DbContextOptionsBuilder<ZWardenDbContext>()
            .UseZWardenProvider(provider, connectionString)
            .Options;
        return new ZWardenDbContext(options, new SingleTenantContext());
    }

    private static string TempDbFile() => Path.Combine(Path.GetTempPath(), $"zw-migrate-{Guid.NewGuid():N}.db");

    private static void TryDelete(string file)
    {
        try
        {
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }
        catch (IOException)
        {
        }
    }
}
