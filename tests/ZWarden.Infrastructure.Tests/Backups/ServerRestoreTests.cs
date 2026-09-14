using Microsoft.EntityFrameworkCore;
using ZWarden.Application.Backups;
using ZWarden.Application.Operations;
using ZWarden.Domain.Authorization;
using ZWarden.Domain.Backups;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Operations;
using ZWarden.Domain.Servers;
using ZWarden.Infrastructure.Authorization;
using ZWarden.Infrastructure.Backups;
using ZWarden.Infrastructure.Persistence;
using ZWarden.Infrastructure.Servers;
using ZWarden.Infrastructure.Tests.Agents;
using ZWarden.TestSupport;

namespace ZWarden.Infrastructure.Tests.Backups;

/// <summary>
/// F25: the restore service, fail-closed (ADR 0018). It authorizes the server-scoped <c>Backup.Restore</c>, resolves
/// the backup through the tenant filter (foreign/unknown ⇒ BackupNotFound), refuses early when the Server was last
/// observed running (ServerRunning), audits, and enqueues a mutating, server-scoped restore Operation on the backup's
/// Agent carrying the archive name and checksum on its payload; a busy server surfaces as ServerBusy. Proven against
/// a real SQLite database, a real <see cref="PermissionChecker"/>, and seeded assignments.
/// </summary>
public class ServerRestoreTests
{
    private static readonly TenantId Tenant = TenantId.New();
    private static readonly DateTimeOffset Now = new(2026, 9, 14, 10, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task Restore_enqueues_a_mutating_restore_operation_with_the_archive_and_checksum_on_the_payload()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            AgentId agent = AgentId.New();
            ServerId serverId = await SeedServerAsync(options, agent, ServerRunState.Stopped);
            BackupId backupId = await SeedBackupAsync(options, serverId, agent, "world-1.tar.gz", "abc123");
            await SeedAssignmentAsync(options, user, serverId, Permissions.BackupRestore);

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            RecordingCoordinator coordinator = new();
            CapturingAuditWriter audit = new();
            ServerRestore sut = Service(db, coordinator, audit);

            RestoreRequestResult result = await sut.RestoreAsync(user, backupId);

            await Assert.That(result.Succeeded).IsTrue();
            await Assert.That(coordinator.LastRequest!.Kind).IsEqualTo(OperationKind.Restore);
            await Assert.That(coordinator.LastRequest!.IsMutating).IsTrue();
            await Assert.That(coordinator.LastRequest!.ServerId).IsEqualTo(serverId);
            await Assert.That(coordinator.LastRequest!.AgentId).IsEqualTo(agent);
            RestoreCommandPayload payload = RestoreCommandPayload.FromJson(coordinator.LastRequest!.CommandPayload!);
            await Assert.That(payload.ArchiveName).IsEqualTo("world-1.tar.gz");
            await Assert.That(payload.Sha256).IsEqualTo("abc123");
            await Assert.That(payload.BackupId).IsEqualTo(backupId.ToString());
            await Assert.That(audit.Actions).Contains(ServerAuditActions.Restored);
        });
    }

    [Test]
    public async Task Restore_denies_without_the_server_scoped_backup_restore_permission()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            AgentId agent = AgentId.New();
            ServerId serverId = await SeedServerAsync(options, agent, ServerRunState.Stopped);
            BackupId backupId = await SeedBackupAsync(options, serverId, agent, "world-1.tar.gz", "abc123");
            // No Backup.Restore assignment.

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            RecordingCoordinator coordinator = new();
            ServerRestore sut = Service(db, coordinator, new CapturingAuditWriter());

            RestoreRequestResult result = await sut.RestoreAsync(user, backupId);

            await Assert.That(result.Failure).IsEqualTo(RestoreRequestFailure.NotAuthorized);
            await Assert.That(coordinator.LastRequest).IsNull();
        });
    }

    [Test]
    public async Task Restore_reports_backup_not_found_for_an_unknown_backup()
    {
        await WithSqlite(async options =>
        {
            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            ServerRestore sut = Service(db, new RecordingCoordinator(), new CapturingAuditWriter());

            RestoreRequestResult result = await sut.RestoreAsync(UserId.New(), BackupId.New());

            await Assert.That(result.Failure).IsEqualTo(RestoreRequestFailure.BackupNotFound);
        });
    }

    [Test]
    public async Task Restore_refuses_when_the_server_was_last_observed_running()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            AgentId agent = AgentId.New();
            ServerId serverId = await SeedServerAsync(options, agent, ServerRunState.Running);
            BackupId backupId = await SeedBackupAsync(options, serverId, agent, "world-1.tar.gz", "abc123");
            await SeedAssignmentAsync(options, user, serverId, Permissions.BackupRestore);

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            RecordingCoordinator coordinator = new();
            ServerRestore sut = Service(db, coordinator, new CapturingAuditWriter());

            RestoreRequestResult result = await sut.RestoreAsync(user, backupId);

            await Assert.That(result.Failure).IsEqualTo(RestoreRequestFailure.ServerRunning);
            await Assert.That(coordinator.LastRequest).IsNull();
        });
    }

    [Test]
    public async Task Restore_reports_server_busy_when_the_per_server_lock_refuses()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            AgentId agent = AgentId.New();
            ServerId serverId = await SeedServerAsync(options, agent, ServerRunState.Stopped);
            BackupId backupId = await SeedBackupAsync(options, serverId, agent, "world-1.tar.gz", "abc123");
            await SeedAssignmentAsync(options, user, serverId, Permissions.BackupRestore);

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            ServerRestore sut = Service(db, new RecordingCoordinator { ThrowBusy = true }, new CapturingAuditWriter());

            RestoreRequestResult result = await sut.RestoreAsync(user, backupId);

            await Assert.That(result.Failure).IsEqualTo(RestoreRequestFailure.ServerBusy);
        });
    }

    private static ServerRestore Service(ZWardenDbContext db, RecordingCoordinator coordinator, CapturingAuditWriter audit)
        => new(
            new ServerRepository(db),
            new BackupRepository(db),
            new PermissionChecker(db, new TestTenantContext(Tenant)),
            coordinator,
            audit);

    private sealed class RecordingCoordinator : IOperationCoordinator
    {
        public bool ThrowBusy { get; init; }

        public EnqueueOperationRequest? LastRequest { get; private set; }

        public Task<Operation> EnqueueAsync(
            EnqueueOperationRequest request, UserId? actor = null, CancellationToken cancellationToken = default)
        {
            if (ThrowBusy)
            {
                throw new ServerBusyException(request.ServerId!.Value);
            }

            LastRequest = request;
            return Task.FromResult(Operation.Enqueue(
                request.AgentId, request.Kind, request.IsMutating, request.IdempotencyKey, Now, request.ServerId,
                request.CommandPayload));
        }

        public Task<Operation> RequestCancellationAsync(
            OperationId operationId, UserId? actor = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private static async Task<ServerId> SeedServerAsync(DbContextOptions options, AgentId agent, ServerRunState state)
    {
        await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
        Server server = Server.Import(agent, ServerId.New(), "survivors", Now);
        server.RecordObservedState(state, Now);
        new ServerRepository(db).Add(server);
        await db.SaveChangesAsync();
        return server.Id;
    }

    private static async Task<BackupId> SeedBackupAsync(
        DbContextOptions options, ServerId serverId, AgentId agent, string archiveName, string sha256)
    {
        await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
        Backup backup = Backup.Record(serverId, agent, archiveName, 100, sha256, BackupReason.Manual, Now);
        new BackupRepository(db).Add(backup);
        await db.SaveChangesAsync();
        return backup.Id;
    }

    private static async Task SeedAssignmentAsync(
        DbContextOptions options, UserId user, ServerId server, PermissionDefinition permission)
    {
        await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
        Role role = Role.CreateCustom(Tenant, $"role-{Guid.NewGuid():N}");
        role.Grant(permission);
        db.Set<Role>().Add(role);
        db.Set<RoleAssignment>().Add(RoleAssignment.ForServer(Tenant, user, role.Id, server));
        await db.SaveChangesAsync();
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
