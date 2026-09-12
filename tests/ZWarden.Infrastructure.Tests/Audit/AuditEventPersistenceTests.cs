using Microsoft.EntityFrameworkCore;
using ZWarden.Domain.Audit;
using ZWarden.Domain.Ids;
using ZWarden.Infrastructure.Audit;
using ZWarden.Infrastructure.Persistence;
using ZWarden.TestSupport;

namespace ZWarden.Infrastructure.Tests.Audit;

/// <summary>
/// F6 S2: the <see cref="AuditEvent"/> store is tenant-owned — stamped and read only through the tenant
/// filter (ADR 0016) — its fields round-trip, and its tenant scope is immutable. Proven against a real
/// SQLite database and a two-tenant fixture. Offline tier.
/// </summary>
public class AuditEventPersistenceTests
{
    private static readonly TenantId TenantA = TenantId.New();
    private static readonly TenantId TenantB = TenantId.New();

    [Test]
    public async Task An_event_is_stamped_with_the_ambient_tenant_and_scoped_by_it()
    {
        await WithSqlite(async options =>
        {
            await using (ZWardenDbContext asA = new(options, new TestTenantContext(TenantA)))
            {
                AuditEventRepository repo = new(asA);
                // Tenant left unset so the ownership interceptor stamps the ambient tenant.
                repo.Add(AuditEvent.Create("Role.Created", AuditOutcome.Succeeded, DateTimeOffset.UnixEpoch));
                await asA.SaveChangesAsync();

                AuditEvent stored = (await repo.ListAsync()).Single();
                await Assert.That(stored.TenantId).IsEqualTo(TenantA);
            }

            await using (ZWardenDbContext asB = new(options, new TestTenantContext(TenantB)))
            {
                AuditEventRepository repo = new(asB);
                await Assert.That(await repo.CountAsync()).IsEqualTo(0);
            }
        });
    }

    [Test]
    public async Task All_fields_round_trip()
    {
        UserId actor = UserId.New();
        ServerId server = ServerId.New();
        DateTimeOffset when = new(2026, 9, 12, 8, 30, 0, TimeSpan.Zero);

        await WithSqlite(async options =>
        {
            await using (ZWardenDbContext asA = new(options, new TestTenantContext(TenantA)))
            {
                asA.Set<AuditEvent>().Add(AuditEvent.Create(
                    "Authentication.SignInSucceeded", AuditOutcome.Succeeded, when,
                    actorUserId: actor, serverId: server, correlationId: "corr-1", detail: "ok"));
                await asA.SaveChangesAsync();
            }

            await using (ZWardenDbContext asA = new(options, new TestTenantContext(TenantA)))
            {
                AuditEvent stored = (await new AuditEventRepository(asA).ListAsync()).Single();
                await Assert.That(stored.Action).IsEqualTo("Authentication.SignInSucceeded");
                await Assert.That(stored.Outcome).IsEqualTo(AuditOutcome.Succeeded);
                await Assert.That(stored.OccurredAt).IsEqualTo(when);
                await Assert.That(stored.ActorUserId).IsEqualTo(actor);
                await Assert.That(stored.ServerId).IsEqualTo(server);
                await Assert.That(stored.CorrelationId).IsEqualTo("corr-1");
                await Assert.That(stored.Detail).IsEqualTo("ok");
            }
        });
    }

    [Test]
    public async Task An_events_tenant_scope_is_immutable()
    {
        await WithSqlite(async options =>
        {
            await using (ZWardenDbContext seed = new(options, new TestTenantContext(TenantA)))
            {
                seed.Set<AuditEvent>().Add(AuditEvent.Create("Role.Created", AuditOutcome.Succeeded, DateTimeOffset.UnixEpoch));
                await seed.SaveChangesAsync();
            }

            await using ZWardenDbContext asA = new(options, new TestTenantContext(TenantA));
            AuditEvent audit = await asA.Set<AuditEvent>().SingleAsync();
            asA.Entry(audit).Property(nameof(AuditEvent.TenantId)).CurrentValue = TenantB;

            await Assert.That(async () => await asA.SaveChangesAsync())
                .Throws<TenantScopeViolationException>();
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
