using Microsoft.EntityFrameworkCore;
using ZWarden.Application.Workshop;
using ZWarden.Domain.Authorization;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Servers;
using ZWarden.Infrastructure.Authorization;
using ZWarden.Infrastructure.Persistence;
using ZWarden.Infrastructure.Servers;
using ZWarden.Infrastructure.Workshop;
using ZWarden.TestSupport;

namespace ZWarden.Infrastructure.Tests.Workshop;

/// <summary>
/// #110 PR-C: the authorized, server-scoped Mod Browser preview service. Fail-closed (ADR 0018) — it resolves the
/// Server through the tenant filter and requires <c>Mod.View</c> before it composes the keyless metadata client, so
/// an unauthorized viewer never drives the control plane's Steam egress. It resolves a pasted id or collection URL,
/// tries a collection first, and never throws. Proven against a real SQLite database + a real
/// <see cref="PermissionChecker"/>, with the keyless client faked (no live call, F12 rule).
/// </summary>
public class WorkshopMetadataServiceTests
{
    private static readonly TenantId Tenant = TenantId.New();
    private static readonly DateTimeOffset Now = new(2026, 9, 18, 10, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task Resolves_a_single_pasted_id_to_item_metadata()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            ServerId serverId = await SeedServerAsync(options, AgentId.New());
            await SeedAssignmentAsync(options, user, serverId, Permissions.ModView);

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            FakeMetadataClient client = new();
            client.Items["2392709985"] = new WorkshopItemMetadata("2392709985", Found: true, Title: "Brita's Weapon Pack");
            WorkshopMetadataService sut = Service(db, client);

            WorkshopPreview preview = await sut.ResolveAsync(user, serverId, "2392709985");

            await Assert.That(preview.Status).IsEqualTo(WorkshopPreviewStatus.Resolved);
            await Assert.That(preview.IsCollection).IsFalse();
            await Assert.That(preview.Items.Single().Title).IsEqualTo("Brita's Weapon Pack");
        });
    }

    [Test]
    public async Task Resolves_a_pasted_item_url()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            ServerId serverId = await SeedServerAsync(options, AgentId.New());
            await SeedAssignmentAsync(options, user, serverId, Permissions.ModView);

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            FakeMetadataClient client = new();
            client.Items["111"] = new WorkshopItemMetadata("111", Found: true, Title: "By URL");
            WorkshopMetadataService sut = Service(db, client);

            WorkshopPreview preview = await sut.ResolveAsync(
                user, serverId, "https://steamcommunity.com/sharedfiles/filedetails/?id=111");

            await Assert.That(preview.Status).IsEqualTo(WorkshopPreviewStatus.Resolved);
            await Assert.That(preview.Items.Single().WorkshopId).IsEqualTo("111");
        });
    }

    [Test]
    public async Task Expands_a_collection_reference_into_its_members()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            ServerId serverId = await SeedServerAsync(options, AgentId.New());
            await SeedAssignmentAsync(options, user, serverId, Permissions.ModView);

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            FakeMetadataClient client = new();
            client.Collections["777"] = ["111", "222"];
            client.Items["111"] = new WorkshopItemMetadata("111", Found: true, Title: "One");
            client.Items["222"] = new WorkshopItemMetadata("222", Found: true, Title: "Two");
            WorkshopMetadataService sut = Service(db, client);

            WorkshopPreview preview = await sut.ResolveAsync(user, serverId, "777");

            await Assert.That(preview.Status).IsEqualTo(WorkshopPreviewStatus.Resolved);
            await Assert.That(preview.IsCollection).IsTrue();
            await Assert.That(preview.Items.Count).IsEqualTo(2);
            await Assert.That(preview.Items[0].WorkshopId).IsEqualTo("111");
        });
    }

    [Test]
    public async Task An_id_steam_cannot_resolve_comes_back_as_a_not_found_item()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            ServerId serverId = await SeedServerAsync(options, AgentId.New());
            await SeedAssignmentAsync(options, user, serverId, Permissions.ModView);

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            WorkshopMetadataService sut = Service(db, new FakeMetadataClient());

            WorkshopPreview preview = await sut.ResolveAsync(user, serverId, "999");

            await Assert.That(preview.Status).IsEqualTo(WorkshopPreviewStatus.Resolved);
            await Assert.That(preview.Items.Single().Found).IsFalse();
            await Assert.That(preview.Items.Single().WorkshopId).IsEqualTo("999");
        });
    }

    [Test]
    public async Task An_input_without_an_id_is_unresolvable_and_makes_no_call()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            ServerId serverId = await SeedServerAsync(options, AgentId.New());
            await SeedAssignmentAsync(options, user, serverId, Permissions.ModView);

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            FakeMetadataClient client = new();
            WorkshopMetadataService sut = Service(db, client);

            WorkshopPreview preview = await sut.ResolveAsync(user, serverId, "not-a-workshop-link");

            await Assert.That(preview.Status).IsEqualTo(WorkshopPreviewStatus.Unresolvable);
            await Assert.That(client.Calls).IsEqualTo(0);
        });
    }

    [Test]
    public async Task Denies_without_mod_view_and_makes_no_call()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            ServerId serverId = await SeedServerAsync(options, AgentId.New());
            // A Mod.Install grant does not authorize a view (Mod.View); the read gate is its own permission.
            await SeedAssignmentAsync(options, user, serverId, Permissions.ModInstall);

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            FakeMetadataClient client = new();
            WorkshopMetadataService sut = Service(db, client);

            WorkshopPreview preview = await sut.ResolveAsync(user, serverId, "111");

            await Assert.That(preview.Status).IsEqualTo(WorkshopPreviewStatus.NotAuthorized);
            await Assert.That(client.Calls).IsEqualTo(0);
        });
    }

    [Test]
    public async Task Reports_server_not_found_for_an_unknown_server()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            ServerId unknown = ServerId.New();
            await SeedAssignmentAsync(options, user, unknown, Permissions.ModView);

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            FakeMetadataClient client = new();
            WorkshopMetadataService sut = Service(db, client);

            WorkshopPreview preview = await sut.ResolveAsync(user, unknown, "111");

            await Assert.That(preview.Status).IsEqualTo(WorkshopPreviewStatus.ServerNotFound);
            await Assert.That(client.Calls).IsEqualTo(0);
        });
    }

    private static WorkshopMetadataService Service(ZWardenDbContext db, FakeMetadataClient client) =>
        new(new ServerRepository(db), new PermissionChecker(db, new TestTenantContext(Tenant)), client);

    private sealed class FakeMetadataClient : IWorkshopMetadataClient
    {
        public Dictionary<string, WorkshopItemMetadata> Items { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, IReadOnlyList<string>> Collections { get; } = new(StringComparer.Ordinal);

        public int Calls { get; private set; }

        public Task<IReadOnlyList<WorkshopItemMetadata>> GetItemsAsync(
            IReadOnlyList<string> workshopIds, CancellationToken cancellationToken = default)
        {
            Calls++;
            IReadOnlyList<WorkshopItemMetadata> result =
                [.. workshopIds.Select(id => Items.TryGetValue(id, out WorkshopItemMetadata? m) ? m : WorkshopItemMetadata.NotFound(id))];
            return Task.FromResult(result);
        }

        public Task<IReadOnlyList<string>> GetCollectionItemIdsAsync(
            string collectionId, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(Collections.TryGetValue(collectionId, out IReadOnlyList<string>? ids) ? ids : []);
        }
    }

    private static async Task<ServerId> SeedServerAsync(DbContextOptions options, AgentId agent)
    {
        await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
        Server server = Server.Import(agent, ServerId.New(), "survivors", Now);
        new ServerRepository(db).Add(server);
        await db.SaveChangesAsync();
        return server.Id;
    }

    private static async Task SeedAssignmentAsync(
        DbContextOptions options, UserId user, ServerId server, PermissionDefinition permission)
    {
        await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
        Role role = Role.CreateCustom(Tenant, $"role-{Guid.NewGuid():N}");
        role.Grant(permission);
        db.Set<Role>().Add(role);
        db.Set<RoleAssignment>().Add(RoleAssignment.ForServer(Tenant, user, role.Id, server));
        await db.SaveChangesAsync();
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
                // Best-effort: a pooled handle may still hold the file briefly on Windows.
            }
        }
    }
}
