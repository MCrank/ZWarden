using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ZWarden.Application.Audit;
using ZWarden.Application.Backups;
using ZWarden.Application.Operations;
using ZWarden.Domain.Backups;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Operations;
using ZWarden.Domain.Servers;
using ZWarden.Infrastructure.Backups;
using ZWarden.Infrastructure.Persistence;
using ZWarden.Infrastructure.Servers;
using ZWarden.Infrastructure.Tests.Agents;
using ZWarden.TestSupport;

namespace ZWarden.Infrastructure.Tests.Backups;

/// <summary>
/// #379: retention keeps a server's newest <c>KeepAutomatic</c> automatic (<c>PreOperation</c>) backups. Each older
/// one is deleted through the existing non-mutating <c>DeleteBackup</c> Operation (its record goes when the Agent
/// confirms) and audited. A Manual backup is never pruned, another server's backups are never touched, and a backup
/// already being deleted isn't deleted twice. Proven against a real SQLite database.
/// </summary>
public class BackupRetentionTests
{
    private static readonly TenantId Tenant = TenantId.New();
    private static readonly DateTimeOffset Now = new(2026, 10, 10, 10, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task Keeps_the_newest_automatic_backups_and_deletes_the_older_ones()
    {
        await WithSqlite(async options =>
        {
            AgentId agent = AgentId.New();
            ServerId serverId = await SeedServerAsync(options, agent);
            // Seeded out of order, so the prune must sort by when each was taken, not by insertion.
            int[] minutes = [3, 0, 5, 1, 4, 2];
            Dictionary<int, BackupId> ids = [];
            foreach (int minute in minutes)
            {
                ids[minute] = await SeedBackupAsync(options, serverId, agent, BackupReason.PreOperation, minute);
            }

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            RecordingCoordinator coordinator = new();
            CapturingAuditWriter audit = new();

            int pruned = await Retention(db, coordinator, audit, keep: 4).PruneAsync(serverId);

            await Assert.That(pruned).IsEqualTo(2);
            await Assert.That(coordinator.Requests.Count).IsEqualTo(2);
            await Assert.That(coordinator.Requests.All(r => r.Kind == OperationKind.DeleteBackup && !r.IsMutating)).IsTrue();
            await Assert.That(coordinator.Requests.All(r => r.AgentId == agent && r.ServerId == serverId)).IsTrue();
            string[] deleted = [.. coordinator.Requests.Select(r => BackupCommandPayload.FromJson(r.CommandPayload!).BackupId!)];
            string[] expected = [ids[0].ToString(), ids[1].ToString()];
            await Assert.That(deleted).IsEquivalentTo(expected);
            await Assert.That(coordinator.Requests.All(r => BackupCommandPayload.FromJson(r.CommandPayload!).ArchiveName!.StartsWith("world-", StringComparison.Ordinal))).IsTrue();
            await Assert.That(coordinator.Actors.All(a => a is null)).IsTrue();
            await Assert.That(audit.Entries.Count).IsEqualTo(2);
            await Assert.That(audit.Entries.All(e => e.Action == ServerAuditActions.BackupPruned && e.ServerId == serverId)).IsTrue();
            await Assert.That(audit.Entries[0].ActorUserId).IsNull();
            await Assert.That(audit.Entries[0].Detail!).Contains("keeps the last 4 automatic backups");
        });
    }

    [Test]
    public async Task Never_prunes_a_manual_backup()
    {
        await WithSqlite(async options =>
        {
            AgentId agent = AgentId.New();
            ServerId serverId = await SeedServerAsync(options, agent);
            for (int minute = 0; minute < 5; minute++)
            {
                await SeedBackupAsync(options, serverId, agent, BackupReason.Manual, minute);
            }

            await SeedBackupAsync(options, serverId, agent, BackupReason.PreOperation, 10);

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            RecordingCoordinator coordinator = new();

            int pruned = await Retention(db, coordinator, new CapturingAuditWriter(), keep: 1).PruneAsync(serverId);

            await Assert.That(pruned).IsEqualTo(0);
            await Assert.That(coordinator.Requests.Count).IsEqualTo(0);
        });
    }

    [Test]
    public async Task Does_nothing_at_or_under_the_limit_and_ignores_other_servers()
    {
        await WithSqlite(async options =>
        {
            AgentId agent = AgentId.New();
            ServerId serverId = await SeedServerAsync(options, agent);
            ServerId otherServer = await SeedServerAsync(options, agent);
            for (int minute = 0; minute < 5; minute++)
            {
                await SeedBackupAsync(options, serverId, agent, BackupReason.PreOperation, minute);
                await SeedBackupAsync(options, otherServer, agent, BackupReason.PreOperation, minute);
            }

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            RecordingCoordinator coordinator = new();

            int pruned = await Retention(db, coordinator, new CapturingAuditWriter(), keep: 5).PruneAsync(serverId);

            await Assert.That(pruned).IsEqualTo(0);
            await Assert.That(coordinator.Requests.Count).IsEqualTo(0);
        });
    }

    [Test]
    public async Task Does_not_delete_a_backup_whose_deletion_is_already_in_flight()
    {
        await WithSqlite(async options =>
        {
            AgentId agent = AgentId.New();
            ServerId serverId = await SeedServerAsync(options, agent);
            BackupId oldest = await SeedBackupAsync(options, serverId, agent, BackupReason.PreOperation, 0);
            BackupId next = await SeedBackupAsync(options, serverId, agent, BackupReason.PreOperation, 1);
            await SeedBackupAsync(options, serverId, agent, BackupReason.PreOperation, 2);
            Operation inFlight = Operation.Enqueue(
                agent, OperationKind.DeleteBackup, isMutating: false, "k", Now, serverId,
                new BackupCommandPayload(BackupId: oldest.ToString(), ArchiveName: "x").ToJson());

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            RecordingCoordinator coordinator = new();

            int pruned = await Retention(db, coordinator, new CapturingAuditWriter(), keep: 1, active: [inFlight])
                .PruneAsync(serverId);

            await Assert.That(pruned).IsEqualTo(1);
            await Assert.That(BackupCommandPayload.FromJson(coordinator.Requests.Single().CommandPayload!).BackupId)
                .IsEqualTo(next.ToString());
        });
    }

    [Test]
    public async Task A_host_that_went_offline_mid_prune_leaves_the_rest_for_the_next_prune()
    {
        await WithSqlite(async options =>
        {
            AgentId agent = AgentId.New();
            ServerId serverId = await SeedServerAsync(options, agent);
            for (int minute = 0; minute < 3; minute++)
            {
                await SeedBackupAsync(options, serverId, agent, BackupReason.PreOperation, minute);
            }

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            CapturingAuditWriter audit = new();

            int pruned = await Retention(db, new RecordingCoordinator { ThrowOffline = true }, audit, keep: 1).PruneAsync(serverId);

            await Assert.That(pruned).IsEqualTo(0);
            await Assert.That(audit.Entries.Count).IsEqualTo(0);
            await Assert.That((await new BackupRepository(db).ListForServerAsync(serverId)).Count).IsEqualTo(3);
        });
    }

    private static BackupRetention Retention(
        ZWardenDbContext db, RecordingCoordinator coordinator, IAuditWriter audit, int keep, IReadOnlyList<Operation>? active = null)
        => new(
            new BackupRepository(db),
            coordinator,
            new ActiveOperations(active ?? []),
            audit,
            Options.Create(new BackupOptions { KeepAutomatic = keep }));

    private sealed class RecordingCoordinator : IOperationCoordinator
    {
        public bool ThrowOffline { get; init; }

        public List<EnqueueOperationRequest> Requests { get; } = [];

        public List<UserId?> Actors { get; } = [];

        public Task<Operation> EnqueueAsync(
            EnqueueOperationRequest request, UserId? actor = null, CancellationToken cancellationToken = default)
        {
            if (ThrowOffline)
            {
                throw new HostOfflineException(request.ServerId!.Value);
            }

            Requests.Add(request);
            Actors.Add(actor);
            return Task.FromResult(Operation.Enqueue(
                request.AgentId, request.Kind, request.IsMutating, request.IdempotencyKey, Now, request.ServerId,
                request.CommandPayload));
        }

        public Task<Operation> RequestCancellationAsync(
            OperationId operationId, UserId? actor = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class ActiveOperations(IReadOnlyList<Operation> active) : IOperationStore
    {
        public Task<IReadOnlyList<Operation>> ListActiveAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(active);

        public Task<Operation?> FindAsync(OperationId operationId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<Operation?> FindActiveForServerAsync(ServerId serverId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<Operation?> FindUnresolvedFailureForServerAsync(ServerId serverId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<Operation?> FindLatestSucceededForServerAsync(
            ServerId serverId, OperationKind kind, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task ApplyProgressAsync(
            OperationId operationId, int percentComplete, string? statusLine, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task CompleteSucceededAsync(OperationId operationId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task CompleteFailedAsync(
            OperationId operationId, string? failureReason, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private static async Task<ServerId> SeedServerAsync(DbContextOptions options, AgentId agent)
    {
        await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
        Server server = Server.Import(agent, ServerId.New(), $"survivors-{Guid.NewGuid():N}", Now);
        new ServerRepository(db).Add(server);
        await db.SaveChangesAsync();
        return server.Id;
    }

    private static async Task<BackupId> SeedBackupAsync(
        DbContextOptions options, ServerId serverId, AgentId agent, BackupReason reason, int minute)
    {
        await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
        Backup backup = Backup.Record(
            serverId, agent, $"world-{minute:D2}-{Guid.NewGuid():N}.tar.gz", 100, "sha", reason, Now.AddMinutes(minute));
        new BackupRepository(db).Add(backup);
        await db.SaveChangesAsync();
        return backup.Id;
    }

    private static async Task WithSqlite(Func<DbContextOptions, Task> body)
    {
        string file = Path.Combine(Path.GetTempPath(), $"zw-{Guid.NewGuid():N}.db");
        DbContextOptions options = new DbContextOptionsBuilder<ZWardenDbContext>()
            .UseZWardenProvider(ZWardenDbProvider.Sqlite, $"Data Source={file};Pooling=False")
            .Options;
        try
        {
            await using (ZWardenDbContext db = new(options, new TestTenantContext(Tenant)))
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
