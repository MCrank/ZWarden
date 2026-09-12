using ZWarden.Application.Authorization;
using ZWarden.Domain.Agents;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Security;
using ZWarden.Infrastructure.Agents;
using ZWarden.Infrastructure.Persistence;

namespace ZWarden.Infrastructure.Tests.Agents;

/// <summary>
/// F9 S4 (PR 1): the operator-facing <see cref="AgentTrustService"/> — rotate, revoke, disable and enable,
/// each gated by <c>Tenant.Enrollment.Manage</c>, audited, and taking effect on the next verification
/// (ADR 0007).
/// </summary>
public class AgentTrustServiceTests
{
    private static (AgentId Id, SecretString Credential) SeedAgent(ZWardenDbContext ctx)
    {
        SecretString cred = TrustTestHarness.Hasher.Generate("zwa");
        Agent agent = Agent.Enroll(TrustTestHarness.Hasher.Hash(cred), EnrollmentId.New(), TrustTestHarness.Now);
        ctx.Add(agent);
        ctx.SaveChanges();
        return (agent.Id, cred);
    }

    private static string[] Manage => [TrustTestHarness.ManagePermission];

    [Test]
    public async Task Rotate_issues_a_new_credential_and_invalidates_the_old()
    {
        await TrustTestHarness.WithSqlite(async options =>
        {
            CapturingAuditWriter audit = new();
            await using ZWardenDbContext ctx = TrustTestHarness.Context(options);
            (AgentId id, SecretString oldCred) = SeedAgent(ctx);

            SecretString newCred = await TrustTestHarness.Trust(ctx, audit, Manage)
                .RotateCredentialAsync(TrustTestHarness.Manager, id);

            AgentCredentialVerifier verifier = TrustTestHarness.Verifier(ctx);
            await Assert.That(await verifier.VerifyAsync(oldCred)).IsNull();
            await Assert.That(await verifier.VerifyAsync(newCred)).IsEqualTo(id);
            await Assert.That(audit.Actions).Contains(EnrollmentAuditActions.CredentialRotated);
        });
    }

    [Test]
    public async Task Revoke_invalidates_the_credential()
    {
        await TrustTestHarness.WithSqlite(async options =>
        {
            CapturingAuditWriter audit = new();
            await using ZWardenDbContext ctx = TrustTestHarness.Context(options);
            (AgentId id, SecretString cred) = SeedAgent(ctx);

            await TrustTestHarness.Trust(ctx, audit, Manage).RevokeCredentialAsync(TrustTestHarness.Manager, id);

            await Assert.That(await TrustTestHarness.Verifier(ctx).VerifyAsync(cred)).IsNull();
            await Assert.That(audit.Actions).Contains(EnrollmentAuditActions.CredentialRevoked);
        });
    }

    [Test]
    public async Task Disable_refuses_even_a_matching_credential_and_enable_restores_it()
    {
        await TrustTestHarness.WithSqlite(async options =>
        {
            CapturingAuditWriter audit = new();
            await using ZWardenDbContext ctx = TrustTestHarness.Context(options);
            (AgentId id, SecretString cred) = SeedAgent(ctx);
            AgentTrustService trust = TrustTestHarness.Trust(ctx, audit, Manage);
            AgentCredentialVerifier verifier = TrustTestHarness.Verifier(ctx);

            await trust.DisableAsync(TrustTestHarness.Manager, id);
            await Assert.That(await verifier.VerifyAsync(cred)).IsNull();

            await trust.EnableAsync(TrustTestHarness.Manager, id);
            await Assert.That(await verifier.VerifyAsync(cred)).IsEqualTo(id);

            await Assert.That(audit.Actions).Contains(EnrollmentAuditActions.AgentDisabled);
            await Assert.That(audit.Actions).Contains(EnrollmentAuditActions.AgentEnabled);
        });
    }

    [Test]
    public async Task Revoking_or_disabling_drops_a_live_connection_and_audits_it()
    {
        await TrustTestHarness.WithSqlite(async options =>
        {
            CapturingAuditWriter audit = new();
            await using ZWardenDbContext ctx = TrustTestHarness.Context(options);
            (AgentId id, _) = SeedAgent(ctx);
            RecordingConnectionRegistry connections = new(abortSucceeds: true);

            await TrustTestHarness.Trust(ctx, audit, Manage, connections)
                .RevokeCredentialAsync(TrustTestHarness.Manager, id);

            // The live connection is aborted immediately (F10), not left until the Agent reconnects.
            await Assert.That(connections.Aborted).Contains(id);
            await Assert.That(audit.Actions).Contains(AgentConnectionAuditActions.CredentialRevokedWhileConnected);
        });
    }

    [Test]
    public async Task Revoking_an_agent_with_no_live_connection_does_not_audit_a_drop()
    {
        await TrustTestHarness.WithSqlite(async options =>
        {
            CapturingAuditWriter audit = new();
            await using ZWardenDbContext ctx = TrustTestHarness.Context(options);
            (AgentId id, _) = SeedAgent(ctx);
            RecordingConnectionRegistry connections = new(abortSucceeds: false);

            await TrustTestHarness.Trust(ctx, audit, Manage, connections)
                .RevokeCredentialAsync(TrustTestHarness.Manager, id);

            await Assert.That(connections.Aborted).Contains(id);
            await Assert.That(audit.Actions).DoesNotContain(AgentConnectionAuditActions.CredentialRevokedWhileConnected);
            await Assert.That(audit.Actions).Contains(EnrollmentAuditActions.CredentialRevoked);
        });
    }

    [Test]
    public async Task Every_operation_requires_the_permission()
    {
        await TrustTestHarness.WithSqlite(async options =>
        {
            CapturingAuditWriter audit = new();
            await using ZWardenDbContext ctx = TrustTestHarness.Context(options);
            (AgentId id, _) = SeedAgent(ctx);
            AgentTrustService denied = TrustTestHarness.Trust(ctx, audit, held: []);

            await Assert.That(async () => await denied.RotateCredentialAsync(TrustTestHarness.Manager, id))
                .Throws<AuthorizationDeniedException>();
            await Assert.That(async () => await denied.RevokeCredentialAsync(TrustTestHarness.Manager, id))
                .Throws<AuthorizationDeniedException>();
            await Assert.That(async () => await denied.DisableAsync(TrustTestHarness.Manager, id))
                .Throws<AuthorizationDeniedException>();
            await Assert.That(audit.Entries).IsEmpty();
        });
    }
}
