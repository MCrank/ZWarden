using ZWarden.Domain.Enrollments;
using ZWarden.Domain.Ids;
using ZWarden.Infrastructure.Agents;
using ZWarden.Infrastructure.Persistence;

namespace ZWarden.Infrastructure.Tests.Agents;

/// <summary>
/// The DEV-ONLY <see cref="DevEnrollmentBootstrapper"/> (#123, ADR 0031): it seeds one well-known
/// redeemable enrollment so an Aspire-orchestrated Agent self-enrolls, is idempotent, re-seeds after the
/// dev enrollment is consumed, and — the load-bearing guard — refuses to seed outside Development.
/// </summary>
public class DevEnrollmentBootstrapperTests
{
    private const string DevSecret = "zwe_dev-aspire-bootstrap-test";
    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(1);

    private static string Hash(string secret) =>
        TrustTestHarness.Hasher.Hash(new Domain.Security.SecretString(secret));

    [Test]
    public async Task Seeds_one_redeemable_enrollment_in_Development()
    {
        await TrustTestHarness.WithSqlite(async options =>
        {
            await using (ZWardenDbContext ctx = TrustTestHarness.Context(options))
            {
                await DevEnrollmentBootstrapper.EnsureDevEnrollmentAsync(
                    ctx, TrustTestHarness.Hasher, TrustTestHarness.Now,
                    isDevelopment: true, DevSecret, Lifetime);
            }

            await using (ZWardenDbContext ctx = TrustTestHarness.Context(options))
            {
                IReadOnlyList<Enrollment> all = await new EnrollmentRepository(ctx).ListAsync();
                await Assert.That(all.Count).IsEqualTo(1);
                await Assert.That(all[0].SecretHash).IsEqualTo(Hash(DevSecret));
                await Assert.That(all[0].IsRedeemable(TrustTestHarness.Now)).IsTrue();
                await Assert.That(all[0].Label).IsEqualTo(DevEnrollmentBootstrapper.DevEnrollmentLabel);
            }
        });
    }

    [Test]
    public async Task Is_idempotent_across_repeated_boots()
    {
        await TrustTestHarness.WithSqlite(async options =>
        {
            for (int i = 0; i < 3; i++)
            {
                await using ZWardenDbContext ctx = TrustTestHarness.Context(options);
                await DevEnrollmentBootstrapper.EnsureDevEnrollmentAsync(
                    ctx, TrustTestHarness.Hasher, TrustTestHarness.Now,
                    isDevelopment: true, DevSecret, Lifetime);
            }

            await using (ZWardenDbContext ctx = TrustTestHarness.Context(options))
            {
                IReadOnlyList<Enrollment> all = await new EnrollmentRepository(ctx).ListAsync();
                await Assert.That(all.Count).IsEqualTo(1);
                await Assert.That(all[0].IsRedeemable(TrustTestHarness.Now)).IsTrue();
            }
        });
    }

    [Test]
    public async Task Re_seeds_a_fresh_redeemable_after_the_previous_was_consumed()
    {
        await TrustTestHarness.WithSqlite(async options =>
        {
            // Seed, then consume it (as the exchange would) so the Agent's trust could be lost while the DB persists.
            await using (ZWardenDbContext ctx = TrustTestHarness.Context(options))
            {
                await DevEnrollmentBootstrapper.EnsureDevEnrollmentAsync(
                    ctx, TrustTestHarness.Hasher, TrustTestHarness.Now,
                    isDevelopment: true, DevSecret, Lifetime);
            }

            await using (ZWardenDbContext ctx = TrustTestHarness.Context(options))
            {
                Enrollment seeded = (await new EnrollmentRepository(ctx).ListAsync()).Single();
                seeded.Consume(AgentId.New(), TrustTestHarness.Now);
                await ctx.SaveChangesAsync();
            }

            // Next boot: the previous is consumed, so a fresh redeemable is minted and the stale one removed.
            await using (ZWardenDbContext ctx = TrustTestHarness.Context(options))
            {
                await DevEnrollmentBootstrapper.EnsureDevEnrollmentAsync(
                    ctx, TrustTestHarness.Hasher, TrustTestHarness.Now,
                    isDevelopment: true, DevSecret, Lifetime);
            }

            await using (ZWardenDbContext ctx = TrustTestHarness.Context(options))
            {
                IReadOnlyList<Enrollment> all = await new EnrollmentRepository(ctx).ListAsync();
                // Exactly one row carries the hash (deterministic exchange lookup) and it is redeemable.
                await Assert.That(all.Count).IsEqualTo(1);
                await Assert.That(all[0].IsRedeemable(TrustTestHarness.Now)).IsTrue();
            }
        });
    }

    [Test]
    public async Task Does_nothing_when_no_secret_is_configured()
    {
        await TrustTestHarness.WithSqlite(async options =>
        {
            await using (ZWardenDbContext ctx = TrustTestHarness.Context(options))
            {
                await DevEnrollmentBootstrapper.EnsureDevEnrollmentAsync(
                    ctx, TrustTestHarness.Hasher, TrustTestHarness.Now,
                    isDevelopment: true, enrollmentSecret: null, Lifetime);
                await DevEnrollmentBootstrapper.EnsureDevEnrollmentAsync(
                    ctx, TrustTestHarness.Hasher, TrustTestHarness.Now,
                    isDevelopment: true, enrollmentSecret: "   ", Lifetime);
            }

            await using (ZWardenDbContext ctx = TrustTestHarness.Context(options))
            {
                await Assert.That((await new EnrollmentRepository(ctx).ListAsync()).Count).IsEqualTo(0);
            }
        });
    }

    [Test]
    public async Task Refuses_to_seed_outside_Development_and_writes_nothing()
    {
        await TrustTestHarness.WithSqlite(async options =>
        {
            await using (ZWardenDbContext ctx = TrustTestHarness.Context(options))
            {
                await Assert.That(async () => await DevEnrollmentBootstrapper.EnsureDevEnrollmentAsync(
                        ctx, TrustTestHarness.Hasher, TrustTestHarness.Now,
                        isDevelopment: false, DevSecret, Lifetime))
                    .Throws<InvalidOperationException>();
            }

            await using (ZWardenDbContext ctx = TrustTestHarness.Context(options))
            {
                await Assert.That((await new EnrollmentRepository(ctx).ListAsync()).Count).IsEqualTo(0);
            }
        });
    }
}
