using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ZWarden.Application.Workshop;
using ZWarden.Domain.Authorization;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Security;
using ZWarden.Domain.Servers;
using ZWarden.Infrastructure.Authorization;
using ZWarden.Infrastructure.Persistence;
using ZWarden.Infrastructure.Servers;
using ZWarden.Infrastructure.Workshop;
using ZWarden.TestSupport;

namespace ZWarden.Infrastructure.Tests.Workshop;

/// <summary>
/// #291 D5: a Workshop item's required items ("children"), offered at Install. Reading them needs the tenant's
/// optional Steam Web API key (<c>IPublishedFileService/GetDetails?includechildren=true</c>); without one nothing is
/// shown and no call is made. Fail-closed on Mod.View; the ids are untrusted JSON, validated and bounded; their
/// details come from the keyless metadata client. Every failure degrades to "no dependencies". Synthetic JSON from a
/// stub handler — no live Steam call (F12 rule).
/// </summary>
public class WorkshopDependencyServiceTests
{
    private const string Key = "ABCDEF0123456789ABCDEF0123456789";
    private static readonly TenantId Tenant = TenantId.New();
    private static readonly DateTimeOffset Now = new(2026, 10, 3, 10, 0, 0, TimeSpan.Zero);

    private const string TwoChildren = """
        {"response":{"publishedfiledetails":[{"result":1,"publishedfileid":"300","children":[
          {"publishedfileid":"2392709985","sortorder":1,"file_type":0},
          {"publishedfileid":"not-digits","sortorder":2,"file_type":0},
          {"publishedfileid":"300","sortorder":3,"file_type":0},
          {"publishedfileid":"2335368829","sortorder":4,"file_type":0}
        ]}]}}
        """;

    [Test]
    public async Task Required_items_are_read_with_the_key_and_returned_with_their_details()
    {
        await WithSqlite(async options =>
        {
            (UserId user, ServerId server) = await SeedViewerAsync(options);
            StubHandler handler = new(HttpStatusCode.OK, TwoChildren);
            FakeMetadata metadata = new();

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            IReadOnlyList<WorkshopItemMetadata> required = await Service(db, handler, metadata, Key)
                .GetRequiredItemsAsync(user, server, "300");

            await Assert.That(handler.LastUri!).Contains("IPublishedFileService/GetDetails");
            await Assert.That(handler.LastUri!).Contains("includechildren=true");
            await Assert.That(handler.LastUri!).Contains("key=" + Key);
            // Non-numeric ids and the item itself are dropped; the rest keep Steam's order.
            await Assert.That(required.Select(r => r.WorkshopId)).IsEquivalentTo(["2392709985", "2335368829"]);
            await Assert.That(required[0].Title).IsEqualTo("Item 2392709985");
        });
    }

    [Test]
    public async Task Without_a_key_nothing_is_offered_and_no_call_is_made()
    {
        await WithSqlite(async options =>
        {
            (UserId user, ServerId server) = await SeedViewerAsync(options);
            StubHandler handler = new(HttpStatusCode.OK, TwoChildren);

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            IReadOnlyList<WorkshopItemMetadata> required = await Service(db, handler, new FakeMetadata(), key: null)
                .GetRequiredItemsAsync(user, server, "300");

            await Assert.That(required).IsEmpty();
            await Assert.That(handler.Calls).IsEqualTo(0);
        });
    }

    [Test]
    public async Task Without_mod_view_nothing_is_offered_and_no_call_is_made()
    {
        await WithSqlite(async options =>
        {
            (_, ServerId server) = await SeedViewerAsync(options);
            StubHandler handler = new(HttpStatusCode.OK, TwoChildren);

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            IReadOnlyList<WorkshopItemMetadata> required = await Service(db, handler, new FakeMetadata(), Key)
                .GetRequiredItemsAsync(UserId.New(), server, "300");

            await Assert.That(required).IsEmpty();
            await Assert.That(handler.Calls).IsEqualTo(0);
        });
    }

    [Test]
    [Arguments(HttpStatusCode.Forbidden, null)]
    [Arguments(HttpStatusCode.OK, "{not json")]
    [Arguments(HttpStatusCode.OK, """{"response":{"publishedfiledetails":[{"result":1,"publishedfileid":"300"}]}}""")]
    public async Task A_rejected_key_bad_json_or_no_children_offers_nothing(HttpStatusCode status, string? json)
    {
        await WithSqlite(async options =>
        {
            (UserId user, ServerId server) = await SeedViewerAsync(options);

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            IReadOnlyList<WorkshopItemMetadata> required = await Service(db, new StubHandler(status, json), new FakeMetadata(), Key)
                .GetRequiredItemsAsync(user, server, "300");

            await Assert.That(required).IsEmpty();
        });
    }

    private static WorkshopDependencyService Service(ZWardenDbContext db, StubHandler handler, FakeMetadata metadata, string? key) =>
        new(
            new HttpClient(handler) { BaseAddress = new Uri("https://api.steampowered.com/") },
            new ServerRepository(db),
            new PermissionChecker(db, new TestTenantContext(Tenant)),
            new FakeWorkshopSettings(key),
            metadata,
            NullLogger<WorkshopDependencyService>.Instance);

    private static async Task<(UserId User, ServerId Server)> SeedViewerAsync(DbContextOptions options)
    {
        await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
        Server server = Server.Import(AgentId.New(), ServerId.New(), "survivors", Now);
        new ServerRepository(db).Add(server);
        UserId user = UserId.New();
        Role role = Role.CreateCustom(Tenant, $"role-{Guid.NewGuid():N}");
        role.Grant(Permissions.ModView);
        db.Set<Role>().Add(role);
        db.Set<RoleAssignment>().Add(RoleAssignment.ForServer(Tenant, user, role.Id, server.Id));
        await db.SaveChangesAsync();
        return (user, server.Id);
    }

    private sealed class FakeMetadata : IWorkshopMetadataClient
    {
        public Task<IReadOnlyList<WorkshopItemMetadata>> GetItemsAsync(
            IReadOnlyList<string> workshopIds, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<WorkshopItemMetadata>>(
                [.. workshopIds.Reverse().Select(id => new WorkshopItemMetadata(id, Found: true, Title: $"Item {id}"))]);

        public Task<IReadOnlyList<WorkshopItemMetadata>> RefreshItemsAsync(
            IReadOnlyList<string> workshopIds, CancellationToken cancellationToken = default) =>
            GetItemsAsync(workshopIds, cancellationToken);

        public Task<IReadOnlyList<string>> GetCollectionItemIdsAsync(
            string collectionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>([]);
    }

    private sealed class StubHandler(HttpStatusCode status, string? json) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        public string? LastUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            LastUri = request.RequestUri?.ToString();
            HttpResponseMessage response = new(status);
            if (json is not null)
            {
                response.Content = new StringContent(json, Encoding.UTF8, "application/json");
            }

            return Task.FromResult(response);
        }
    }

    private sealed class FakeWorkshopSettings(string? key) : IWorkshopSettingsService
    {
        public Task<SecretString?> GetActiveApiKeyAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<SecretString?>(key is null ? null : new SecretString(key));

        public Task SetApiKeyAsync(UserId actor, SecretString apiKey, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task ClearApiKeyAsync(UserId actor, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> IsSearchAvailableAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(key is not null);
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
