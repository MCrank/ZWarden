using Microsoft.EntityFrameworkCore;
using ZWarden.Application.Backups;
using ZWarden.Application.Operations;
using ZWarden.Domain.Backups;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Operations;
using ZWarden.Domain.Servers;
using ZWarden.Infrastructure.Backups;
using ZWarden.Infrastructure.Persistence;
using ZWarden.Infrastructure.Servers;
using ZWarden.TestSupport;

namespace ZWarden.Infrastructure.Tests.Backups;

/// <summary>
/// F24: the completion-ingest recorder. A successful backup is persisted as a tenant-owned <see cref="Backup"/>
/// with the retention reason read from the Operation's command payload — but only against a Server the reporting
/// Agent owns (trust-boundaries §3). A confirmed deletion removes the record the Operation's payload names. Proven
/// against a real SQLite database with a two-tenant-safe fixture.
/// </summary>
public class BackupRecorderTests
{
    private static readonly TenantId Tenant = TenantId.New();
    private static readonly DateTimeOffset Now = new(2026, 9, 14, 10, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task RecordCreated_persists_a_backup_with_the_reason_from_the_operation_payload()
    {
        await WithSqlite(async options =>
        {
            AgentId agent = AgentId.New();
            ServerId serverId = await SeedServerAsync(options, agent);
            OperationId operationId = OperationId.New();
            Operation operation = Operation.Enqueue(
                agent, OperationKind.Backup, isMutating: true, "k", Now, serverId,
                new BackupCommandPayload(Reason: nameof(BackupReason.PreOperation)).ToJson());

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            BackupRecorder sut = Recorder(db, new FakeOperationStore(operationId, operation));

            await sut.RecordCreatedAsync(serverId, agent, operationId, "world-1.tar.gz", 4096, "sha", Now);

            IReadOnlyList<Backup> backups = await new BackupRepository(db).ListForServerAsync(serverId);
            await Assert.That(backups.Count).IsEqualTo(1);
            await Assert.That(backups[0].ArchiveName).IsEqualTo("world-1.tar.gz");
            await Assert.That(backups[0].SizeBytes).IsEqualTo(4096L);
            await Assert.That(backups[0].Sha256).IsEqualTo("sha");
            await Assert.That(backups[0].Reason).IsEqualTo(BackupReason.PreOperation);
        });
    }

    [Test]
    public async Task RecordCreated_is_a_no_op_when_the_reporting_agent_does_not_own_the_server()
    {
        await WithSqlite(async options =>
        {
            AgentId owner = AgentId.New();
            AgentId other = AgentId.New();
            ServerId serverId = await SeedServerAsync(options, owner);

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            BackupRecorder sut = Recorder(db, new FakeOperationStore(OperationId.New(), operation: null));

            await sut.RecordCreatedAsync(serverId, other, OperationId.New(), "world-1.tar.gz", 1, "sha", Now);

            await Assert.That((await new BackupRepository(db).ListForServerAsync(serverId)).Count).IsEqualTo(0);
        });
    }

    [Test]
    public async Task RecordDeleted_removes_the_record_named_by_the_operation_payload()
    {
        await WithSqlite(async options =>
        {
            AgentId agent = AgentId.New();
            ServerId serverId = await SeedServerAsync(options, agent);
            BackupId backupId = await SeedBackupAsync(options, serverId, agent);
            OperationId operationId = OperationId.New();
            Operation operation = Operation.Enqueue(
                agent, OperationKind.DeleteBackup, isMutating: false, "k", Now, serverId,
                new BackupCommandPayload(BackupId: backupId.ToString(), ArchiveName: "world-1.tar.gz").ToJson());

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            BackupRecorder sut = Recorder(db, new FakeOperationStore(operationId, operation));

            await sut.RecordDeletedAsync(operationId);

            await Assert.That(await new BackupRepository(db).FindByIdAsync(backupId)).IsNull();
        });
    }

    [Test]
    public async Task RecordRestoreProtectiveBackup_persists_a_pre_operation_backup_for_the_owning_agent()
    {
        await WithSqlite(async options =>
        {
            AgentId agent = AgentId.New();
            ServerId serverId = await SeedServerAsync(options, agent);

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            BackupRecorder sut = Recorder(db, new FakeOperationStore(OperationId.New(), operation: null));

            await sut.RecordRestoreProtectiveBackupAsync(
                serverId, agent, "world-1-pre-restore.tar.gz", 2048, "protectivesha", Now);

            IReadOnlyList<Backup> backups = await new BackupRepository(db).ListForServerAsync(serverId);
            await Assert.That(backups.Count).IsEqualTo(1);
            await Assert.That(backups[0].ArchiveName).IsEqualTo("world-1-pre-restore.tar.gz");
            await Assert.That(backups[0].Sha256).IsEqualTo("protectivesha");
            await Assert.That(backups[0].Reason).IsEqualTo(BackupReason.PreOperation);
        });
    }

    [Test]
    public async Task RecordRestoreProtectiveBackup_is_a_no_op_when_the_reporting_agent_does_not_own_the_server()
    {
        await WithSqlite(async options =>
        {
            AgentId owner = AgentId.New();
            AgentId other = AgentId.New();
            ServerId serverId = await SeedServerAsync(options, owner);

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            BackupRecorder sut = Recorder(db, new FakeOperationStore(OperationId.New(), operation: null));

            await sut.RecordRestoreProtectiveBackupAsync(serverId, other, "world-1-pre-restore.tar.gz", 1, "sha", Now);

            await Assert.That((await new BackupRepository(db).ListForServerAsync(serverId)).Count).IsEqualTo(0);
        });
    }

    private static BackupRecorder Recorder(ZWardenDbContext db, IOperationStore operations)
        => new(operations, new ServerRepository(db), new BackupRepository(db), db);

    private sealed class FakeOperationStore : IOperationStore
    {
        private readonly OperationId _id;
        private readonly Operation? _operation;

        public FakeOperationStore(OperationId id, Operation? operation)
        {
            _id = id;
            _operation = operation;
        }

        public Task<Operation?> FindAsync(OperationId operationId, CancellationToken cancellationToken = default)
            => Task.FromResult(operationId == _id ? _operation : null);

        public Task<Operation?> FindActiveForServerAsync(ServerId serverId, CancellationToken cancellationToken = default)
            => Task.FromResult<Operation?>(null);

        public Task<IReadOnlyList<Operation>> ListActiveAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Operation>>([]);

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
        Server server = Server.Import(agent, ServerId.New(), "survivors", Now);
        new ServerRepository(db).Add(server);
        await db.SaveChangesAsync();
        return server.Id;
    }

    private static async Task<BackupId> SeedBackupAsync(DbContextOptions options, ServerId serverId, AgentId agent)
    {
        await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
        Backup backup = Backup.Record(serverId, agent, "world-1.tar.gz", 1, "sha", BackupReason.Manual, Now);
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
