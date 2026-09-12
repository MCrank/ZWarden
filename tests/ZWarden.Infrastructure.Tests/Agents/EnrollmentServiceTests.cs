using ZWarden.Application.Agents;
using ZWarden.Application.Authorization;
using ZWarden.Domain.Enrollments;
using ZWarden.Domain.Security;
using ZWarden.Infrastructure.Agents;
using ZWarden.Infrastructure.Persistence;

namespace ZWarden.Infrastructure.Tests.Agents;

/// <summary>
/// F9 S4 (PR 1): the operator-facing <see cref="EnrollmentService"/> is authorized against
/// <c>Tenant.Enrollment.Manage</c> (fail-closed), audits, and stores only the secret's hash (decision 2).
/// </summary>
public class EnrollmentServiceTests
{
    [Test]
    public async Task Issue_denies_an_actor_without_the_permission_and_writes_nothing()
    {
        await TrustTestHarness.WithSqlite(async options =>
        {
            CapturingAuditWriter audit = new();
            await using ZWardenDbContext ctx = TrustTestHarness.Context(options);
            EnrollmentService service = TrustTestHarness.Enrollment(ctx, audit, held: []);

            await Assert.That(async () => await service.IssueAsync(TrustTestHarness.Manager))
                .Throws<AuthorizationDeniedException>();
            await Assert.That(await new EnrollmentRepository(ctx).CountAsync()).IsEqualTo(0);
            await Assert.That(audit.Entries).IsEmpty();
        });
    }

    [Test]
    public async Task Issue_returns_the_secret_once_stores_only_its_hash_and_audits()
    {
        await TrustTestHarness.WithSqlite(async options =>
        {
            CapturingAuditWriter audit = new();
            await using ZWardenDbContext ctx = TrustTestHarness.Context(options);
            EnrollmentService service = TrustTestHarness.Enrollment(ctx, audit, held: [TrustTestHarness.ManagePermission]);

            EnrollmentTokenResult result = await service.IssueAsync(TrustTestHarness.Manager, label: "host-alpha");

            await Assert.That(result.Secret.HasValue).IsTrue();
            await Assert.That(result.Label).IsEqualTo("host-alpha");

            Enrollment stored = (await new EnrollmentRepository(ctx).FindByIdAsync(result.EnrollmentId))!;
            await Assert.That(stored.SecretHash).IsNotEqualTo(result.Secret.Reveal()); // only the hash is stored
            await Assert.That(stored.SecretHash).IsEqualTo(TrustTestHarness.Hasher.Hash(result.Secret));
            await Assert.That(stored.Status).IsEqualTo(EnrollmentStatus.Pending);
            await Assert.That(audit.Actions).Contains(EnrollmentAuditActions.TokenIssued);
        });
    }

    [Test]
    public async Task Issue_rejects_a_lifetime_beyond_the_maximum()
    {
        await TrustTestHarness.WithSqlite(async options =>
        {
            CapturingAuditWriter audit = new();
            await using ZWardenDbContext ctx = TrustTestHarness.Context(options);
            EnrollmentService service = TrustTestHarness.Enrollment(ctx, audit, held: [TrustTestHarness.ManagePermission]);

            await Assert.That(async () => await service.IssueAsync(TrustTestHarness.Manager, TimeSpan.FromDays(30)))
                .Throws<ArgumentOutOfRangeException>();
        });
    }

    [Test]
    public async Task Revoke_cancels_a_pending_enrollment_and_audits()
    {
        await TrustTestHarness.WithSqlite(async options =>
        {
            CapturingAuditWriter audit = new();
            await using ZWardenDbContext ctx = TrustTestHarness.Context(options);
            EnrollmentService service = TrustTestHarness.Enrollment(ctx, audit, held: [TrustTestHarness.ManagePermission]);

            EnrollmentTokenResult result = await service.IssueAsync(TrustTestHarness.Manager);
            await service.RevokeAsync(TrustTestHarness.Manager, result.EnrollmentId);

            Enrollment stored = (await new EnrollmentRepository(ctx).FindByIdAsync(result.EnrollmentId))!;
            await Assert.That(stored.Status).IsEqualTo(EnrollmentStatus.Revoked);
            await Assert.That(audit.Actions).Contains(EnrollmentAuditActions.Revoked);
        });
    }
}
