using ZWarden.Application.Agents;
using ZWarden.Domain.Agents;
using ZWarden.Domain.Enrollments;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Security;
using ZWarden.Infrastructure.Agents;
using ZWarden.Infrastructure.Persistence;

namespace ZWarden.Infrastructure.Tests.Agents;

/// <summary>
/// F9 S4 (PR 1): the pre-trust <see cref="AgentEnrollmentExchange"/> — a valid secret creates a trusted
/// Agent and consumes the enrollment single-use; every failure returns the same generic result (the reason
/// is audited, not disclosed). No permission gate — the secret is the authorization (ADR 0007).
/// </summary>
public class AgentEnrollmentExchangeTests
{
    private static SecretString IssuedSecret(ZWardenDbContext ctx, CapturingAuditWriter audit, DateTimeOffset? now = null)
    {
        EnrollmentService service = TrustTestHarness.Enrollment(ctx, audit, held: [TrustTestHarness.ManagePermission], now: now);
        return service.IssueAsync(TrustTestHarness.Manager).GetAwaiter().GetResult().Secret;
    }

    [Test]
    public async Task A_valid_secret_creates_a_trusted_agent_and_consumes_the_enrollment()
    {
        await TrustTestHarness.WithSqlite(async options =>
        {
            CapturingAuditWriter audit = new();
            AgentId agentId;
            await using (ZWardenDbContext ctx = TrustTestHarness.Context(options))
            {
                SecretString secret = IssuedSecret(ctx, audit);
                AgentEnrollmentResult result = await TrustTestHarness.Exchange(ctx, audit).RedeemAsync(secret);

                await Assert.That(result.Succeeded).IsTrue();
                await Assert.That(result.Credential.HasValue).IsTrue();
                agentId = result.AgentId!.Value;
            }

            await using (ZWardenDbContext ctx = TrustTestHarness.Context(options))
            {
                Agent agent = (await new AgentRepository(ctx).FindByIdAsync(agentId))!;
                await Assert.That(agent.IsTrusted).IsTrue();
                Enrollment enrollment = (await new EnrollmentRepository(ctx).ListAsync()).Single();
                await Assert.That(enrollment.Status).IsEqualTo(EnrollmentStatus.Consumed);
                await Assert.That(enrollment.ConsumedByAgent).IsEqualTo(agentId);
            }

            await Assert.That(audit.Actions).Contains(EnrollmentAuditActions.Redeemed);
            await Assert.That(audit.Actions).Contains(EnrollmentAuditActions.AgentEnrolled);
        });
    }

    [Test]
    public async Task An_unknown_secret_fails_generically_and_audits_the_reason()
    {
        await TrustTestHarness.WithSqlite(async options =>
        {
            CapturingAuditWriter audit = new();
            await using ZWardenDbContext ctx = TrustTestHarness.Context(options);

            AgentEnrollmentResult result = await TrustTestHarness.Exchange(ctx, audit)
                .RedeemAsync(TrustTestHarness.Hasher.Generate("zwe"));

            await Assert.That(result.Succeeded).IsFalse();
            await Assert.That(result.Failure).IsEqualTo(EnrollmentRedemptionFailure.UnknownSecret);
            await Assert.That(await new AgentRepository(ctx).CountAsync()).IsEqualTo(0);
            await Assert.That(audit.Actions).Contains(EnrollmentAuditActions.RedemptionFailed);
        });
    }

    [Test]
    public async Task An_expired_secret_fails_as_expired()
    {
        await TrustTestHarness.WithSqlite(async options =>
        {
            CapturingAuditWriter audit = new();
            await using ZWardenDbContext ctx = TrustTestHarness.Context(options);
            SecretString secret = IssuedSecret(ctx, audit);

            // Exchange clock is well past the default 1-hour lifetime.
            AgentEnrollmentResult result = await TrustTestHarness
                .Exchange(ctx, audit, TrustTestHarness.Now.AddHours(2)).RedeemAsync(secret);

            await Assert.That(result.Succeeded).IsFalse();
            await Assert.That(result.Failure).IsEqualTo(EnrollmentRedemptionFailure.Expired);
        });
    }

    [Test]
    public async Task A_revoked_secret_fails_as_revoked()
    {
        await TrustTestHarness.WithSqlite(async options =>
        {
            CapturingAuditWriter audit = new();
            await using ZWardenDbContext ctx = TrustTestHarness.Context(options);
            EnrollmentService enrollment = TrustTestHarness.Enrollment(ctx, audit, held: [TrustTestHarness.ManagePermission]);
            EnrollmentTokenResult issued = await enrollment.IssueAsync(TrustTestHarness.Manager);
            await enrollment.RevokeAsync(TrustTestHarness.Manager, issued.EnrollmentId);

            AgentEnrollmentResult result = await TrustTestHarness.Exchange(ctx, audit).RedeemAsync(issued.Secret);

            await Assert.That(result.Succeeded).IsFalse();
            await Assert.That(result.Failure).IsEqualTo(EnrollmentRedemptionFailure.Revoked);
        });
    }

    [Test]
    public async Task A_second_redemption_fails_and_no_second_agent_is_created()
    {
        await TrustTestHarness.WithSqlite(async options =>
        {
            CapturingAuditWriter audit = new();
            SecretString secret;
            await using (ZWardenDbContext ctx = TrustTestHarness.Context(options))
            {
                secret = IssuedSecret(ctx, audit);
                AgentEnrollmentResult first = await TrustTestHarness.Exchange(ctx, audit).RedeemAsync(secret);
                await Assert.That(first.Succeeded).IsTrue();
            }

            await using (ZWardenDbContext ctx = TrustTestHarness.Context(options))
            {
                AgentEnrollmentResult second = await TrustTestHarness.Exchange(ctx, audit).RedeemAsync(secret);
                await Assert.That(second.Succeeded).IsFalse();
                await Assert.That(second.Failure).IsEqualTo(EnrollmentRedemptionFailure.AlreadyConsumed);
                await Assert.That(await new AgentRepository(ctx).CountAsync()).IsEqualTo(1);
            }
        });
    }
}
