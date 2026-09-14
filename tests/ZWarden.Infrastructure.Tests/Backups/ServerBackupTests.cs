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
/// F24: the backup service, fail-closed (ADR 0018). A take authorizes the server-scoped <c>Backup.Create</c>,
/// resolves the Server through the tenant filter (foreign/unknown ⇒ ServerNotFound), audits, and enqueues a
/// mutating, server-scoped backup Operation carrying the retention reason on its payload; a busy server surfaces as
/// ServerBusy. A delete authorizes <c>Backup.Delete</c>, resolves the backup, and enqueues a non-mutating deletion
/// Operation on the backup's Agent. Proven against a real SQLite database, a real <see cref="PermissionChecker"/>,
/// and seeded assignments.
/// </summary>
public class ServerBackupTests
{
    private static readonly TenantId Tenant = TenantId.New();
    private static readonly DateTimeOffset Now = new(2026, 9, 14, 10, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task Create_enqueues_a_mutating_backup_operation_with_the_reason_on_the_payload()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            AgentId agent = AgentId.New();
            ServerId serverId = await SeedServerAsync(options, agent);
            await SeedAssignmentAsync(options, user, serverId, Permissions.BackupCreate);

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            RecordingCoordinator coordinator = new();
            CapturingAuditWriter audit = new();
            ServerBackup sut = Service(db, coordinator, audit);

            BackupRequestResult result = await sut.CreateAsync(user, serverId, BackupReason.Manual);

            await Assert.That(result.Succeeded).IsTrue();
            await Assert.That(coordinator.LastRequest!.Kind).IsEqualTo(OperationKind.Backup);
            await Assert.That(coordinator.LastRequest!.IsMutating).IsTrue();
            await Assert.That(coordinator.LastRequest!.ServerId).IsEqualTo(serverId);
            await Assert.That(coordinator.LastRequest!.AgentId).IsEqualTo(agent);
            await Assert.That(BackupCommandPayload.FromJson(coordinator.LastRequest!.CommandPayload!).Reason)
                .IsEqualTo(nameof(BackupReason.Manual));
            await Assert.That(audit.Actions).Contains(ServerAuditActions.BackedUp);
        });
    }

    [Test]
    public async Task EnsureBackup_enqueues_a_pre_operation_reason()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            AgentId agent = AgentId.New();
            ServerId serverId = await SeedServerAsync(options, agent);
            await SeedAssignmentAsync(options, user, serverId, Permissions.BackupCreate);

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            RecordingCoordinator coordinator = new();
            ServerBackup sut = Service(db, coordinator, new CapturingAuditWriter());

            BackupRequestResult result = await sut.EnsureBackupAsync(user, serverId);

            await Assert.That(result.Succeeded).IsTrue();
            await Assert.That(BackupCommandPayload.FromJson(coordinator.LastRequest!.CommandPayload!).Reason)
                .IsEqualTo(nameof(BackupReason.PreOperation));
        });
    }

    [Test]
    public async Task Create_denies_without_the_server_scoped_backup_create_permission()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            AgentId agent = AgentId.New();
            ServerId serverId = await SeedServerAsync(options, agent);
            // No Backup.Create assignment.

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            RecordingCoordinator coordinator = new();
            ServerBackup sut = Service(db, coordinator, new CapturingAuditWriter());

            BackupRequestResult result = await sut.CreateAsync(user, serverId, BackupReason.Manual);

            await Assert.That(result.Failure).IsEqualTo(BackupRequestFailure.NotAuthorized);
            await Assert.That(coordinator.LastRequest).IsNull();
        });
    }

    [Test]
    public async Task Create_reports_server_not_found_for_an_unknown_server()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            ServerId unknown = ServerId.New();
            await SeedAssignmentAsync(options, user, unknown, Permissions.BackupCreate);

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            ServerBackup sut = Service(db, new RecordingCoordinator(), new CapturingAuditWriter());

            BackupRequestResult result = await sut.CreateAsync(user, unknown, BackupReason.Manual);

            await Assert.That(result.Failure).IsEqualTo(BackupRequestFailure.ServerNotFound);
        });
    }

    [Test]
    public async Task Create_reports_server_busy_when_the_per_server_lock_refuses()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            AgentId agent = AgentId.New();
            ServerId serverId = await SeedServerAsync(options, agent);
            await SeedAssignmentAsync(options, user, serverId, Permissions.BackupCreate);

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            ServerBackup sut = Service(db, new RecordingCoordinator { ThrowBusy = true }, new CapturingAuditWriter());

            BackupRequestResult result = await sut.CreateAsync(user, serverId, BackupReason.Manual);

            await Assert.That(result.Failure).IsEqualTo(BackupRequestFailure.ServerBusy);
        });
    }

    [Test]
    public async Task Delete_enqueues_a_non_mutating_deletion_on_the_backups_agent()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            AgentId agent = AgentId.New();
            ServerId serverId = await SeedServerAsync(options, agent);
            BackupId backupId = await SeedBackupAsync(options, serverId, agent, "world-1.tar.gz");
            await SeedAssignmentAsync(options, user, serverId, Permissions.BackupDelete);

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            RecordingCoordinator coordinator = new();
            CapturingAuditWriter audit = new();
            ServerBackup sut = Service(db, coordinator, audit);

            BackupDeletionOutcome result = await sut.DeleteAsync(user, backupId);

            await Assert.That(result.Succeeded).IsTrue();
            await Assert.That(coordinator.LastRequest!.Kind).IsEqualTo(OperationKind.DeleteBackup);
            await Assert.That(coordinator.LastRequest!.IsMutating).IsFalse();
            await Assert.That(coordinator.LastRequest!.AgentId).IsEqualTo(agent);
            BackupCommandPayload payload = BackupCommandPayload.FromJson(coordinator.LastRequest!.CommandPayload!);
            await Assert.That(payload.ArchiveName).IsEqualTo("world-1.tar.gz");
            await Assert.That(payload.BackupId).IsEqualTo(backupId.ToString());
            await Assert.That(audit.Actions).Contains(ServerAuditActions.BackupDeleted);
        });
    }

    [Test]
    public async Task Delete_reports_backup_not_found_for_an_unknown_backup()
    {
        await WithSqlite(async options =>
        {
            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            ServerBackup sut = Service(db, new RecordingCoordinator(), new CapturingAuditWriter());

            BackupDeletionOutcome result = await sut.DeleteAsync(UserId.New(), BackupId.New());

            await Assert.That(result.Failure).IsEqualTo(BackupDeletionFailure.BackupNotFound);
        });
    }

    [Test]
    public async Task Delete_denies_without_the_server_scoped_backup_delete_permission()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            AgentId agent = AgentId.New();
            ServerId serverId = await SeedServerAsync(options, agent);
            BackupId backupId = await SeedBackupAsync(options, serverId, agent, "world-1.tar.gz");
            // No Backup.Delete assignment.

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            RecordingCoordinator coordinator = new();
            ServerBackup sut = Service(db, coordinator, new CapturingAuditWriter());

            BackupDeletionOutcome result = await sut.DeleteAsync(user, backupId);

            await Assert.That(result.Failure).IsEqualTo(BackupDeletionFailure.NotAuthorized);
            await Assert.That(coordinator.LastRequest).IsNull();
        });
    }

    private static ServerBackup Service(ZWardenDbContext db, RecordingCoordinator coordinator, CapturingAuditWriter audit)
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

    private static async Task<ServerId> SeedServerAsync(DbContextOptions options, AgentId agent)
    {
        await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
        Server server = Server.Import(agent, ServerId.New(), "survivors", Now);
        new ServerRepository(db).Add(server);
        await db.SaveChangesAsync();
        return server.Id;
    }

    private static async Task<BackupId> SeedBackupAsync(
        DbContextOptions options, ServerId serverId, AgentId agent, string archiveName)
    {
        await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
        Backup backup = Backup.Record(serverId, agent, archiveName, 100, "sha", BackupReason.Manual, Now);
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
