using Microsoft.EntityFrameworkCore;
using ZWarden.Application.Audit;
using ZWarden.Domain.Audit;
using ZWarden.Domain.Ids;
using ZWarden.Infrastructure.Audit;
using ZWarden.Infrastructure.Persistence;
using ZWarden.TestSupport;

namespace ZWarden.Infrastructure.Tests.Audit;

/// <summary>
/// F6 S3: <see cref="AuditWriter"/> appends an event stamped with the clock's <c>OccurredAt</c>, the ambient
/// correlation id, and the ambient tenant, and only ever adds (append-only, ADR 0019). Offline tier.
/// </summary>
public class AuditWriterTests
{
    private static readonly TenantId TenantA = TenantId.New();

    [Test]
    public async Task Write_appends_with_the_clock_correlation_and_ambient_tenant()
    {
        DateTimeOffset fixedNow = new(2026, 9, 12, 10, 0, 0, TimeSpan.Zero);
        UserId actor = UserId.New();

        await WithSqlite(async options =>
        {
            await using (ZWardenDbContext asA = new(options, new TestTenantContext(TenantA)))
            {
                AuditWriter writer = new(asA, new StubTimeProvider(fixedNow), new StubCorrelation("corr-abc"));
                await writer.WriteAsync(new AuditEntry("Role.Created", AuditOutcome.Succeeded, ActorUserId: actor));
            }

            await using (ZWardenDbContext asA = new(options, new TestTenantContext(TenantA)))
            {
                AuditEvent stored = (await new AuditEventRepository(asA).ListAsync()).Single();
                await Assert.That(stored.OccurredAt).IsEqualTo(fixedNow);
                await Assert.That(stored.CorrelationId).IsEqualTo("corr-abc");
                await Assert.That(stored.TenantId).IsEqualTo(TenantA);
                await Assert.That(stored.Action).IsEqualTo("Role.Created");
                await Assert.That(stored.Outcome).IsEqualTo(AuditOutcome.Succeeded);
                await Assert.That(stored.ActorUserId).IsEqualTo(actor);
            }
        });
    }

    [Test]
    public async Task Write_allows_a_null_correlation()
    {
        await WithSqlite(async options =>
        {
            await using (ZWardenDbContext asA = new(options, new TestTenantContext(TenantA)))
            {
                AuditWriter writer = new(asA, new StubTimeProvider(DateTimeOffset.UnixEpoch), new StubCorrelation(null));
                await writer.WriteAsync(new AuditEntry("Role.Created", AuditOutcome.Succeeded));
            }

            await using (ZWardenDbContext asA = new(options, new TestTenantContext(TenantA)))
            {
                AuditEvent stored = (await new AuditEventRepository(asA).ListAsync()).Single();
                await Assert.That(stored.CorrelationId).IsNull();
            }
        });
    }

    private sealed class StubTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;
        public StubTimeProvider(DateTimeOffset now) => _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
    }

    private sealed class StubCorrelation : ICorrelationContext
    {
        public StubCorrelation(string? id) => CurrentCorrelationId = id;
        public string? CurrentCorrelationId { get; }
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
