using Microsoft.EntityFrameworkCore;
using ZWarden.Application.Audit;
using ZWarden.Domain.Audit;
using ZWarden.Domain.Ids;
using ZWarden.Infrastructure.Audit;
using ZWarden.Infrastructure.Persistence;
using ZWarden.TestSupport;

namespace ZWarden.Infrastructure.Tests.Audit;

/// <summary>
/// F6 S4: <see cref="AuditQueryService"/> filters the audit trail (time, action, actor, server, outcome,
/// correlation), returns it newest-first and paged, and never returns another tenant's events (ADR 0016).
/// Offline tier, two-tenant fixture.
/// </summary>
public class AuditQueryServiceTests
{
    private static readonly TenantId TenantA = TenantId.New();
    private static readonly TenantId TenantB = TenantId.New();
    private static readonly DateTimeOffset T0 = new(2026, 9, 12, 8, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task Filters_by_action_actor_server_outcome_and_correlation()
    {
        UserId actor = UserId.New();
        ServerId server = ServerId.New();

        await WithSeededTenantA(async service =>
        {
            await Assert.That((await service.QueryAsync(new AuditQuery(Action: "Role.Created"))).Count).IsEqualTo(1);
            await Assert.That((await service.QueryAsync(new AuditQuery(Outcome: AuditOutcome.Failed))).Count).IsEqualTo(1);

            IReadOnlyList<AuditEventView> byActor = await service.QueryAsync(new AuditQuery(ActorUserId: actor));
            await Assert.That(byActor.Count).IsEqualTo(1);
            await Assert.That(byActor[0].Action).IsEqualTo("Authentication.SignInSucceeded");

            IReadOnlyList<AuditEventView> byServer = await service.QueryAsync(new AuditQuery(ServerId: server));
            await Assert.That(byServer.Count).IsEqualTo(1);
            await Assert.That(byServer[0].Action).IsEqualTo("Server.Started");

            await Assert.That((await service.QueryAsync(new AuditQuery(CorrelationId: "corr-1"))).Count).IsEqualTo(2);
        }, actor, server);
    }

    [Test]
    public async Task Filters_by_time_range()
    {
        await WithSeededTenantA(async service =>
        {
            IReadOnlyList<AuditEventView> window = await service.QueryAsync(
                new AuditQuery(From: T0.AddMinutes(1), To: T0.AddMinutes(2)));

            // The events at T0+1 and T0+2 (two of the four seeded), inclusive of both bounds.
            await Assert.That(window.Count).IsEqualTo(2);
        }, UserId.New(), ServerId.New());
    }

    [Test]
    public async Task Returns_newest_first_and_pages()
    {
        await WithSeededTenantA(async service =>
        {
            IReadOnlyList<AuditEventView> all = await service.QueryAsync(new AuditQuery());
            await Assert.That(all.Count).IsEqualTo(4);
            // Newest first: the last-seeded (T0+3) is first.
            await Assert.That(all[0].OccurredAt).IsEqualTo(T0.AddMinutes(3));
            await Assert.That(all[3].OccurredAt).IsEqualTo(T0);

            IReadOnlyList<AuditEventView> page = await service.QueryAsync(new AuditQuery(Skip: 1, Take: 2));
            await Assert.That(page.Count).IsEqualTo(2);
            await Assert.That(page[0].OccurredAt).IsEqualTo(T0.AddMinutes(2));
            await Assert.That(await service.CountAsync(new AuditQuery())).IsEqualTo(4);
        }, UserId.New(), ServerId.New());
    }

    [Test]
    public async Task A_tenant_never_sees_another_tenants_events()
    {
        await WithSqlite(async options =>
        {
            await using (ZWardenDbContext asB = new(options, new TestTenantContext(TenantB)))
            {
                asB.Set<AuditEvent>().Add(AuditEvent.Create("Role.Created", AuditOutcome.Succeeded, T0));
                await asB.SaveChangesAsync();
            }

            await using (ZWardenDbContext asA = new(options, new TestTenantContext(TenantA)))
            {
                AuditQueryService service = new(new AuditEventRepository(asA));
                await Assert.That((await service.QueryAsync(new AuditQuery())).Count).IsEqualTo(0);
                await Assert.That(await service.CountAsync(new AuditQuery())).IsEqualTo(0);
            }
        });
    }

    // Seeds tenant A with four events at T0..T0+3 and hands a query service scoped to A.
    private static async Task WithSeededTenantA(Func<AuditQueryService, Task> body, UserId actor, ServerId server)
    {
        await WithSqlite(async options =>
        {
            await using (ZWardenDbContext asA = new(options, new TestTenantContext(TenantA)))
            {
                asA.Set<AuditEvent>().Add(AuditEvent.Create(
                    "Authentication.SignInSucceeded", AuditOutcome.Succeeded, T0, actorUserId: actor, correlationId: "corr-1"));
                asA.Set<AuditEvent>().Add(AuditEvent.Create(
                    "Authentication.SignInFailed", AuditOutcome.Failed, T0.AddMinutes(1), correlationId: "corr-1"));
                asA.Set<AuditEvent>().Add(AuditEvent.Create(
                    "Role.Created", AuditOutcome.Succeeded, T0.AddMinutes(2)));
                asA.Set<AuditEvent>().Add(AuditEvent.Create(
                    "Server.Started", AuditOutcome.Succeeded, T0.AddMinutes(3), serverId: server));
                await asA.SaveChangesAsync();
            }

            await using (ZWardenDbContext asA = new(options, new TestTenantContext(TenantA)))
            {
                await body(new AuditQueryService(new AuditEventRepository(asA)));
            }
        });
    }

    private static async Task WithSqlite(Func<DbContextOptions, Task> body)
    {
        string file = Path.Combine(Path.GetTempPath(), $"zw-{Guid.NewGuid():N}.db");
        DbContextOptions options = new DbContextOptionsBuilder<ZWardenDbContext>()
            .UseZWardenProvider(ZWardenDbProvider.Sqlite, $"Data Source={file};Pooling=False")
            .Options;
        try
        {
            await using (ZWardenDbContext db = new(options, new TestTenantContext(TenantA)))
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
