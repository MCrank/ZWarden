using ZWarden.Domain;
using ZWarden.Domain.Enrollments;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Tenancy;

namespace ZWarden.Domain.Tests.Enrollments;

/// <summary>
/// F9 S1 (PR 1): the one-time, short-lived <see cref="Enrollment"/> credential (PRD 63A; ADR 0007) — the
/// <c>enr-</c> record. Pure invariants only; the tenant stamping/filtering is proven in Infrastructure.Tests.
/// The secret itself never lives on the entity — only its one-way hash (decision 2, hash-only).
/// </summary>
public class EnrollmentTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 12, 10, 0, 0, TimeSpan.Zero);
    private const string Hash = "5e884898da28047151d0e56f8dc6292773603d0d6aabbdd62a11ef721d1542d8";

    private static Enrollment Issue(DateTimeOffset? createdAt = null, DateTimeOffset? expiresAt = null) =>
        Enrollment.Issue(
            Hash,
            UserId.New(),
            createdAt ?? Now,
            expiresAt ?? Now.AddMinutes(15));

    [Test]
    public async Task Enrollment_is_tenant_owned_and_versioned()
    {
        await Assert.That(typeof(ITenantOwned).IsAssignableFrom(typeof(Enrollment))).IsTrue();
        await Assert.That(typeof(IVersioned).IsAssignableFrom(typeof(Enrollment))).IsTrue();
    }

    [Test]
    public async Task Issue_creates_a_pending_enrollment()
    {
        Enrollment enrollment = Issue();

        await Assert.That(enrollment.Id.IsEmpty).IsFalse();
        await Assert.That(enrollment.Status).IsEqualTo(EnrollmentStatus.Pending);
        await Assert.That(enrollment.SecretHash).IsEqualTo(Hash);
        await Assert.That(enrollment.ExpiresAt).IsEqualTo(Now.AddMinutes(15));
        await Assert.That(enrollment.ConsumedByAgent).IsNull();
        await Assert.That(enrollment.ConsumedAt).IsNull();
    }

    [Test]
    public async Task Issue_carries_an_optional_label()
    {
        Enrollment labelled = Enrollment.Issue(Hash, UserId.New(), Now, Now.AddMinutes(15), "host-alpha");
        await Assert.That(labelled.Label).IsEqualTo("host-alpha");
        await Assert.That(Issue().Label).IsNull();
    }

    [Test]
    public async Task Issue_rejects_a_blank_secret_hash()
    {
        await Assert.That(() => Enrollment.Issue("  ", UserId.New(), Now, Now.AddMinutes(15)))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task Issue_rejects_an_expiry_not_after_creation()
    {
        await Assert.That(() => Enrollment.Issue(Hash, UserId.New(), Now, Now))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task A_pending_unexpired_enrollment_is_redeemable()
    {
        await Assert.That(Issue().IsRedeemable(Now.AddMinutes(1))).IsTrue();
    }

    [Test]
    public async Task An_expired_enrollment_is_not_redeemable()
    {
        await Assert.That(Issue().IsRedeemable(Now.AddMinutes(15))).IsFalse();
        await Assert.That(Issue().IsRedeemable(Now.AddHours(1))).IsFalse();
    }

    [Test]
    public async Task Consume_marks_it_consumed_and_binds_the_agent()
    {
        Enrollment enrollment = Issue();
        AgentId agent = AgentId.New();

        enrollment.Consume(agent, Now.AddMinutes(2));

        await Assert.That(enrollment.Status).IsEqualTo(EnrollmentStatus.Consumed);
        await Assert.That(enrollment.ConsumedByAgent).IsEqualTo(agent);
        await Assert.That(enrollment.ConsumedAt).IsEqualTo(Now.AddMinutes(2));
        await Assert.That(enrollment.IsRedeemable(Now.AddMinutes(3))).IsFalse();
    }

    [Test]
    public async Task Consume_rejects_an_expired_enrollment()
    {
        Enrollment enrollment = Issue();
        await Assert.That(() => enrollment.Consume(AgentId.New(), Now.AddHours(1)))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Consume_rejects_an_already_consumed_enrollment()
    {
        Enrollment enrollment = Issue();
        enrollment.Consume(AgentId.New(), Now.AddMinutes(1));

        await Assert.That(() => enrollment.Consume(AgentId.New(), Now.AddMinutes(2)))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Revoke_marks_it_revoked_and_blocks_redemption()
    {
        Enrollment enrollment = Issue();

        enrollment.Revoke();

        await Assert.That(enrollment.Status).IsEqualTo(EnrollmentStatus.Revoked);
        await Assert.That(enrollment.IsRedeemable(Now.AddMinutes(2))).IsFalse();
    }

    [Test]
    public async Task Revoke_rejects_an_already_consumed_enrollment()
    {
        Enrollment enrollment = Issue();
        enrollment.Consume(AgentId.New(), Now.AddMinutes(1));

        await Assert.That(() => enrollment.Revoke())
            .Throws<InvalidOperationException>();
    }
}
