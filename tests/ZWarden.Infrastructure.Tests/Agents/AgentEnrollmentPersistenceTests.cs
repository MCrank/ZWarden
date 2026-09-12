using Microsoft.EntityFrameworkCore;
using ZWarden.Domain.Agents;
using ZWarden.Domain.Enrollments;
using ZWarden.Domain.Ids;
using ZWarden.Infrastructure.Agents;
using ZWarden.Infrastructure.Persistence;
using ZWarden.TestSupport;

namespace ZWarden.Infrastructure.Tests.Agents;

/// <summary>
/// F9 S3 (PR 1): the <see cref="Enrollment"/> and <see cref="Agent"/> records are tenant-owned and read only
/// through the tenant filter (ADR 0016; trust-boundaries §9 rule 4). Only the one-way hash is stored
/// (decision 2), and the exchange/verifier hash lookups round-trip. Proven against a real SQLite database
/// with a two-tenant fixture. Offline tier; the same behaviours run on PostgreSQL on the networked tier.
/// </summary>
public class AgentEnrollmentPersistenceTests
{
    private static readonly TenantId TenantA = TenantId.New();
    private static readonly TenantId TenantB = TenantId.New();
    private static readonly DateTimeOffset Now = new(2026, 9, 12, 10, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task An_enrollment_is_stamped_with_the_ambient_tenant_and_scoped_by_it()
    {
        await WithSqlite(async options =>
        {
            const string hash = "enrollmenthashaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
            await using (ZWardenDbContext asA = new(options, new TestTenantContext(TenantA)))
            {
                EnrollmentRepository repo = new(asA);
                repo.Add(Enrollment.Issue(hash, UserId.New(), Now, Now.AddMinutes(15), "host-alpha"));
                await asA.SaveChangesAsync();

                Enrollment? found = await repo.FindBySecretHashAsync(hash);
                await Assert.That(found).IsNotNull();
                await Assert.That(found!.TenantId).IsEqualTo(TenantA);
                await Assert.That(found.Label).IsEqualTo("host-alpha");
            }

            await using (ZWardenDbContext asB = new(options, new TestTenantContext(TenantB)))
            {
                EnrollmentRepository repo = new(asB);
                await Assert.That(await repo.CountAsync()).IsEqualTo(0);
                await Assert.That(await repo.FindBySecretHashAsync(hash)).IsNull();
            }
        });
    }

    [Test]
    public async Task A_consumed_enrollment_round_trips_its_state()
    {
        await WithSqlite(async options =>
        {
            const string hash = "consumedhashbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
            AgentId agent = AgentId.New();

            await using (ZWardenDbContext asA = new(options, new TestTenantContext(TenantA)))
            {
                Enrollment enrollment = Enrollment.Issue(hash, UserId.New(), Now, Now.AddMinutes(15));
                enrollment.Consume(agent, Now.AddMinutes(1));
                asA.Set<Enrollment>().Add(enrollment);
                await asA.SaveChangesAsync();
            }

            await using (ZWardenDbContext asA = new(options, new TestTenantContext(TenantA)))
            {
                Enrollment stored = (await new EnrollmentRepository(asA).FindBySecretHashAsync(hash))!;
                await Assert.That(stored.Status).IsEqualTo(EnrollmentStatus.Consumed);
                await Assert.That(stored.ConsumedByAgent).IsEqualTo(agent);
                await Assert.That(stored.ConsumedAt).IsEqualTo(Now.AddMinutes(1));
                await Assert.That(stored.SecretHash).IsEqualTo(hash);
            }
        });
    }

    [Test]
    public async Task An_agent_is_stamped_with_the_ambient_tenant_and_scoped_by_it()
    {
        await WithSqlite(async options =>
        {
            const string hash = "agenthashccccccccccccccccccccccccccccccccccccccccccccccccccccccc";
            await using (ZWardenDbContext asA = new(options, new TestTenantContext(TenantA)))
            {
                AgentRepository repo = new(asA);
                repo.Add(Agent.Enroll(hash, EnrollmentId.New(), Now, "host-alpha"));
                await asA.SaveChangesAsync();

                Agent? found = await repo.FindByCredentialHashAsync(hash);
                await Assert.That(found).IsNotNull();
                await Assert.That(found!.TenantId).IsEqualTo(TenantA);
                await Assert.That(found.IsTrusted).IsTrue();
            }

            await using (ZWardenDbContext asB = new(options, new TestTenantContext(TenantB)))
            {
                AgentRepository repo = new(asB);
                await Assert.That(await repo.CountAsync()).IsEqualTo(0);
                await Assert.That(await repo.FindByCredentialHashAsync(hash)).IsNull();
            }
        });
    }

    [Test]
    public async Task Rotating_and_revoking_a_credential_persist()
    {
        await WithSqlite(async options =>
        {
            const string original = "originalhashddddddddddddddddddddddddddddddddddddddddddddddddddd";
            const string rotated = "rotatedhasheeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee";
            AgentId id;

            await using (ZWardenDbContext asA = new(options, new TestTenantContext(TenantA)))
            {
                Agent agent = Agent.Enroll(original, EnrollmentId.New(), Now);
                id = agent.Id;
                asA.Set<Agent>().Add(agent);
                await asA.SaveChangesAsync();
            }

            await using (ZWardenDbContext asA = new(options, new TestTenantContext(TenantA)))
            {
                AgentRepository repo = new(asA);
                Agent agent = (await repo.FindByIdAsync(id))!;
                agent.RotateCredential(rotated, Now.AddDays(1));
                await asA.SaveChangesAsync();

                await Assert.That(await repo.FindByCredentialHashAsync(original)).IsNull();
                await Assert.That(await repo.FindByCredentialHashAsync(rotated)).IsNotNull();
            }

            await using (ZWardenDbContext asA = new(options, new TestTenantContext(TenantA)))
            {
                AgentRepository repo = new(asA);
                Agent agent = (await repo.FindByIdAsync(id))!;
                agent.RevokeCredential(Now.AddDays(2));
                await asA.SaveChangesAsync();

                Agent revoked = (await repo.FindByIdAsync(id))!;
                await Assert.That(revoked.CredentialHash).IsEqualTo(string.Empty);
                await Assert.That(revoked.IsTrusted).IsFalse();
                await Assert.That(await repo.FindByCredentialHashAsync(rotated)).IsNull();
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
