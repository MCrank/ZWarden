using Microsoft.EntityFrameworkCore;
using ZWarden.Application.Agents;
using ZWarden.Application.Audit;
using ZWarden.Application.Authorization;
using ZWarden.Application.Settings;
using ZWarden.Domain.Agents;
using ZWarden.Domain.Backups;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Security;
using ZWarden.Domain.Servers;
using ZWarden.Domain.Settings;
using ZWarden.Infrastructure.Agents;
using ZWarden.Infrastructure.Persistence;
using ZWarden.TestSupport;

namespace ZWarden.Infrastructure.Tests.Agents;

/// <summary>
/// #368 (ADR 0049): the Owner replaces a wiped Host (the predecessor) with the Host its machine enrolled as next (the
/// successor). That is the one path across the ownership guard. It needs <c>Tenant.Enrollment.Manage</c>, both
/// Hosts in the tenant, the predecessor offline, and the successor's own snapshot reporting containers stamped with the
/// predecessor's id. Then Servers and backups move, the predecessor is removed, the successor inherits its id (and the
/// ids it had inherited), and its Agent is told to pick the change up.
/// </summary>
public class HostReplacementServiceTests
{
    private static string[] Manage => [TrustTestHarness.ManagePermission];

    private static (AgentId Id, SecretString Credential) SeedAgent(ZWardenDbContext ctx, string hostname)
    {
        SecretString cred = TrustTestHarness.Hasher.Generate("zwa");
        Agent agent = Agent.Enroll(TrustTestHarness.Hasher.Hash(cred), EnrollmentId.New(), TrustTestHarness.Now);
        agent.RecordHostDescriptor(hostname, "1.0.0", "Linux");
        ctx.Add(agent);
        ctx.SaveChanges();
        return (agent.Id, cred);
    }

    private sealed class Rig
    {
        public CapturingAuditWriter Audit { get; } = new();
        public ConnectedRegistry Connections { get; } = new();
        public ForeignContainerCache Foreign { get; } = new();
        public RecordingOwnershipNotifier Notifier { get; } = new();
        public RecordingSettingsCache Settings { get; } = new();

        public HostReplacementService Service(ZWardenDbContext ctx, string[] held) => new(
            ctx,
            new AgentRepository(ctx),
            new StubPermissionChecker(held),
            Audit,
            new StubClock(TrustTestHarness.Now),
            Connections,
            Foreign,
            Notifier,
            Settings,
            new TestTenantContext(TrustTestHarness.Tenant));

        public void Reports(AgentId successor, params AgentId[] labelled) =>
            Foreign.Record(successor, labelled.Select(l => new ReportedForeignContainer("0123456789ab", ServerId.New(), l, "running")).ToList());
    }

    [Test]
    public async Task Replace_moves_servers_and_backups_removes_the_old_host_and_audits()
    {
        await TrustTestHarness.WithSqlite(async options =>
        {
            Rig rig = new();
            await using ZWardenDbContext ctx = TrustTestHarness.Context(options);
            (AgentId old, SecretString oldCred) = SeedAgent(ctx, "nsfw-2-old");
            (AgentId fresh, _) = SeedAgent(ctx, "nsfw-2");
            Server alpha = Server.Register(old, "alpha", TrustTestHarness.Now);
            Server elsewhere = Server.Register(AgentId.New(), "elsewhere", TrustTestHarness.Now);
            Backup backup = Backup.Record(alpha.Id, old, "w.tar.gz", 1, "aa", BackupReason.Manual, TrustTestHarness.Now);
            ctx.AddRange(alpha, elsewhere, backup);
            await ctx.SaveChangesAsync();
            rig.Reports(fresh, old);

            HostReplacementResult result = await rig.Service(ctx, Manage).ReplaceAsync(TrustTestHarness.Manager, fresh, old);

            await Assert.That(result.Outcome).IsEqualTo(HostReplacementOutcome.Replaced);
            await Assert.That(result.ServerCount).IsEqualTo(1);
            await Assert.That(result.BackupCount).IsEqualTo(1);

            await using ZWardenDbContext check = TrustTestHarness.Context(options);
            await Assert.That((await check.Set<Server>().SingleAsync(s => s.Id == alpha.Id)).AgentId).IsEqualTo(fresh);
            await Assert.That((await check.Set<Server>().SingleAsync(s => s.Id == elsewhere.Id)).AgentId).IsNotEqualTo(fresh);
            await Assert.That((await check.Set<Backup>().SingleAsync()).AgentId).IsEqualTo(fresh);
            await Assert.That(await new AgentRepository(check).FindByIdAsync(old)).IsNull();
            await Assert.That(await TrustTestHarness.Verifier(check).VerifyAsync(oldCred)).IsNull();

            AuditEntry replaced = rig.Audit.Entries.Single(e => e.Action == EnrollmentAuditActions.AgentReplaced);
            await Assert.That(replaced.ActorUserId).IsEqualTo(TrustTestHarness.Manager);
            await Assert.That(replaced.Detail!).Contains(fresh.ToString());
            await Assert.That(replaced.Detail!).Contains(old.ToString());
            await Assert.That(replaced.Detail!).Contains("nsfw-2-old");
            await Assert.That(replaced.Detail!).Contains("1 server(s), 1 backup(s)");
        });
    }

    [Test]
    public async Task Replace_hands_the_old_id_to_the_new_host_and_tells_its_agent()
    {
        await TrustTestHarness.WithSqlite(async options =>
        {
            Rig rig = new();
            await using ZWardenDbContext ctx = TrustTestHarness.Context(options);
            (AgentId old, _) = SeedAgent(ctx, "old");
            (AgentId fresh, _) = SeedAgent(ctx, "fresh");
            rig.Reports(fresh, old);
            HostReplacementService service = rig.Service(ctx, Manage);

            await service.ReplaceAsync(TrustTestHarness.Manager, fresh, old);

            await Assert.That(await service.InheritedIdsAsync(fresh)).IsEquivalentTo([old]);
            await Assert.That(await service.InheritedIdsAsync(old)).IsEmpty();
            await Assert.That(rig.Notifier.Notified).IsEquivalentTo([fresh]);
            await Assert.That(rig.Foreign.GetReported(fresh)).IsEmpty();
        });
    }

    [Test]
    public async Task Replacing_the_successor_in_turn_passes_on_every_inherited_id()
    {
        await TrustTestHarness.WithSqlite(async options =>
        {
            Rig rig = new();
            await using ZWardenDbContext ctx = TrustTestHarness.Context(options);
            (AgentId a, _) = SeedAgent(ctx, "a");
            (AgentId b, _) = SeedAgent(ctx, "b");
            (AgentId c, _) = SeedAgent(ctx, "c");
            HostReplacementService service = rig.Service(ctx, Manage);
            rig.Reports(b, a);
            await service.ReplaceAsync(TrustTestHarness.Manager, b, a);

            rig.Reports(c, a, b);
            HostReplacementResult result = await service.ReplaceAsync(TrustTestHarness.Manager, c, b);

            await Assert.That(result.Outcome).IsEqualTo(HostReplacementOutcome.Replaced);
            await Assert.That(await service.InheritedIdsAsync(c)).IsEquivalentTo([a, b]);
            await Assert.That(await service.InheritedIdsAsync(b)).IsEmpty();
        });
    }

    [Test]
    public async Task Replace_moves_the_default_deploy_host_to_the_new_host()
    {
        await TrustTestHarness.WithSqlite(async options =>
        {
            Rig rig = new();
            await using ZWardenDbContext ctx = TrustTestHarness.Context(options);
            (AgentId old, _) = SeedAgent(ctx, "old");
            (AgentId fresh, _) = SeedAgent(ctx, "fresh");
            ControlPlaneSettings settings = ControlPlaneSettings.Create();
            settings.SetDefaultDeployHost(old);
            ctx.Add(settings);
            await ctx.SaveChangesAsync();
            rig.Reports(fresh, old);

            await rig.Service(ctx, Manage).ReplaceAsync(TrustTestHarness.Manager, fresh, old);

            await using ZWardenDbContext check = TrustTestHarness.Context(options);
            await Assert.That((await check.Set<ControlPlaneSettings>().SingleAsync()).DefaultDeployHost).IsEqualTo(fresh);
            await Assert.That(rig.Settings.Remembered!.DefaultDeployHost).IsEqualTo(fresh);
        });
    }

    [Test]
    public async Task Replace_is_refused_when_the_new_host_has_not_reported_the_old_hosts_containers()
    {
        await TrustTestHarness.WithSqlite(async options =>
        {
            Rig rig = new();
            await using ZWardenDbContext ctx = TrustTestHarness.Context(options);
            (AgentId old, SecretString oldCred) = SeedAgent(ctx, "old");
            (AgentId fresh, _) = SeedAgent(ctx, "fresh");
            ctx.Add(Server.Register(old, "alpha", TrustTestHarness.Now));
            await ctx.SaveChangesAsync();
            rig.Reports(fresh, AgentId.New());

            HostReplacementResult result = await rig.Service(ctx, Manage).ReplaceAsync(TrustTestHarness.Manager, fresh, old);

            await Assert.That(result.Outcome).IsEqualTo(HostReplacementOutcome.NotReported);
            await AssertNothingChanged(options, rig, old, oldCred);
        });
    }

    [Test]
    public async Task Replace_is_refused_while_the_old_host_is_online()
    {
        await TrustTestHarness.WithSqlite(async options =>
        {
            Rig rig = new();
            await using ZWardenDbContext ctx = TrustTestHarness.Context(options);
            (AgentId old, SecretString oldCred) = SeedAgent(ctx, "old");
            (AgentId fresh, _) = SeedAgent(ctx, "fresh");
            ctx.Add(Server.Register(old, "alpha", TrustTestHarness.Now));
            await ctx.SaveChangesAsync();
            rig.Reports(fresh, old);
            rig.Connections.Online.Add(old);

            HostReplacementResult result = await rig.Service(ctx, Manage).ReplaceAsync(TrustTestHarness.Manager, fresh, old);

            await Assert.That(result.Outcome).IsEqualTo(HostReplacementOutcome.PredecessorOnline);
            await AssertNothingChanged(options, rig, old, oldCred);
        });
    }

    [Test]
    public async Task A_host_cannot_replace_itself()
    {
        await TrustTestHarness.WithSqlite(async options =>
        {
            Rig rig = new();
            await using ZWardenDbContext ctx = TrustTestHarness.Context(options);
            (AgentId host, SecretString cred) = SeedAgent(ctx, "host");
            rig.Reports(host, host);

            HostReplacementResult result = await rig.Service(ctx, Manage).ReplaceAsync(TrustTestHarness.Manager, host, host);

            await Assert.That(result.Outcome).IsEqualTo(HostReplacementOutcome.SameHost);
            await AssertNothingChanged(options, rig, host, cred);
        });
    }

    [Test]
    public async Task Replace_requires_the_permission()
    {
        await TrustTestHarness.WithSqlite(async options =>
        {
            Rig rig = new();
            await using ZWardenDbContext ctx = TrustTestHarness.Context(options);
            (AgentId old, SecretString oldCred) = SeedAgent(ctx, "old");
            (AgentId fresh, _) = SeedAgent(ctx, "fresh");
            rig.Reports(fresh, old);

            await Assert.That(async () => await rig.Service(ctx, held: []).ReplaceAsync(TrustTestHarness.Manager, fresh, old))
                .Throws<AuthorizationDeniedException>();
            await AssertNothingChanged(options, rig, old, oldCred);
        });
    }

    [Test]
    public async Task Replace_cannot_reach_another_tenants_host()
    {
        await TrustTestHarness.WithSqlite(async options =>
        {
            Rig rig = new();
            AgentId old;
            SecretString oldCred;
            await using (ZWardenDbContext owner = TrustTestHarness.Context(options))
            {
                (old, oldCred) = SeedAgent(owner, "old");
            }

            await using ZWardenDbContext other = new(options, new TestTenantContext(TenantId.New()));
            (AgentId fresh, _) = SeedAgent(other, "fresh");
            rig.Reports(fresh, old);

            await Assert.That(async () => await rig.Service(other, Manage).ReplaceAsync(TrustTestHarness.Manager, fresh, old))
                .Throws<AuthorizationDeniedException>();
            await AssertNothingChanged(options, rig, old, oldCred);
        });
    }

    private static async Task AssertNothingChanged(DbContextOptions options, Rig rig, AgentId old, SecretString oldCred)
    {
        await using ZWardenDbContext check = TrustTestHarness.Context(options);
        await Assert.That(await new AgentRepository(check).FindByIdAsync(old)).IsNotNull();
        await Assert.That(await TrustTestHarness.Verifier(check).VerifyAsync(oldCred)).IsEqualTo(old);
        await Assert.That(await check.Set<Server>().AnyAsync(s => s.AgentId != old)).IsFalse();
        await Assert.That(await check.Set<HostReplacement>().AnyAsync()).IsFalse();
        await Assert.That(rig.Audit.Entries).IsEmpty();
        await Assert.That(rig.Notifier.Notified).IsEmpty();
    }
}

/// <summary>A connection registry where the test decides which Agents are online.</summary>
internal sealed class ConnectedRegistry : IAgentConnectionRegistry
{
    public HashSet<AgentId> Online { get; } = [];

    public List<AgentId> Aborted { get; } = [];

    public void Register(AgentId agentId, string connectionId, Action abort) { }

    public void Remove(string connectionId) { }

    public bool IsConnected(AgentId agentId) => Online.Contains(agentId);

    public string? GetConnectionId(AgentId agentId) => Online.Contains(agentId) ? $"conn-{agentId}" : null;

    public bool TryAbort(AgentId agentId)
    {
        Aborted.Add(agentId);
        return Online.Contains(agentId);
    }
}

internal sealed class RecordingOwnershipNotifier : IAgentOwnershipNotifier
{
    public List<AgentId> Notified { get; } = [];

    public Task OwnershipChangedAsync(AgentId agentId, CancellationToken cancellationToken = default)
    {
        Notified.Add(agentId);
        return Task.CompletedTask;
    }
}

internal sealed class RecordingSettingsCache : IControlPlaneSettingsCache
{
    public ControlPlaneSettingsSnapshot? Remembered { get; private set; }

    public Task<ControlPlaneSettingsSnapshot> GetAsync(TenantId tenant, CancellationToken cancellationToken = default) =>
        Task.FromResult(Remembered ?? ControlPlaneSettingsSnapshot.Empty);

    public void Remember(TenantId tenant, ControlPlaneSettingsSnapshot snapshot) => Remembered = snapshot;
}
