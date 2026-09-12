using ZWarden.Domain.Agents;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Security;
using ZWarden.Infrastructure.Agents;
using ZWarden.Infrastructure.Persistence;

namespace ZWarden.Infrastructure.Tests.Agents;

/// <summary>
/// F9 S4 (PR 1): the <see cref="AgentCredentialVerifier"/> — the seam F10 calls — is fail-closed: it returns
/// the id only for an enabled Agent with a matching credential, and <c>null</c> for a wrong, disabled,
/// revoked, unknown or empty credential (ADR 0007).
/// </summary>
public class AgentCredentialVerifierTests
{
    private static (AgentId Id, SecretString Credential) SeedAgent(ZWardenDbContext ctx)
    {
        SecretString cred = TrustTestHarness.Hasher.Generate("zwa");
        Agent agent = Agent.Enroll(TrustTestHarness.Hasher.Hash(cred), EnrollmentId.New(), TrustTestHarness.Now);
        ctx.Add(agent);
        ctx.SaveChanges();
        return (agent.Id, cred);
    }

    [Test]
    public async Task Accepts_an_enabled_agent_with_a_matching_credential()
    {
        await TrustTestHarness.WithSqlite(async options =>
        {
            await using ZWardenDbContext ctx = TrustTestHarness.Context(options);
            (AgentId id, SecretString cred) = SeedAgent(ctx);

            await Assert.That(await TrustTestHarness.Verifier(ctx).VerifyAsync(cred)).IsEqualTo(id);
        });
    }

    [Test]
    public async Task Rejects_a_wrong_credential()
    {
        await TrustTestHarness.WithSqlite(async options =>
        {
            await using ZWardenDbContext ctx = TrustTestHarness.Context(options);
            SeedAgent(ctx);

            await Assert.That(await TrustTestHarness.Verifier(ctx).VerifyAsync(TrustTestHarness.Hasher.Generate("zwa")))
                .IsNull();
        });
    }

    [Test]
    public async Task Rejects_a_disabled_agent()
    {
        await TrustTestHarness.WithSqlite(async options =>
        {
            await using ZWardenDbContext ctx = TrustTestHarness.Context(options);
            (AgentId id, SecretString cred) = SeedAgent(ctx);

            Agent agent = (await new AgentRepository(ctx).FindByIdAsync(id))!;
            agent.Disable();
            await ctx.SaveChangesAsync();

            await Assert.That(await TrustTestHarness.Verifier(ctx).VerifyAsync(cred)).IsNull();
        });
    }

    [Test]
    public async Task Rejects_a_revoked_credential()
    {
        await TrustTestHarness.WithSqlite(async options =>
        {
            await using ZWardenDbContext ctx = TrustTestHarness.Context(options);
            (AgentId id, SecretString cred) = SeedAgent(ctx);

            Agent agent = (await new AgentRepository(ctx).FindByIdAsync(id))!;
            agent.RevokeCredential(TrustTestHarness.Now.AddHours(1));
            await ctx.SaveChangesAsync();

            await Assert.That(await TrustTestHarness.Verifier(ctx).VerifyAsync(cred)).IsNull();
        });
    }

    [Test]
    public async Task Rejects_an_empty_credential()
    {
        await TrustTestHarness.WithSqlite(async options =>
        {
            await using ZWardenDbContext ctx = TrustTestHarness.Context(options);
            SeedAgent(ctx);

            await Assert.That(await TrustTestHarness.Verifier(ctx).VerifyAsync(default)).IsNull();
        });
    }
}
